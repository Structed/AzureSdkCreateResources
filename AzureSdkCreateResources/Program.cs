// See https://aka.ms/new-console-template for more information

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;

var accountId = Guid.NewGuid().ToString();

var tenantId = Environment.GetEnvironmentVariable("AZ_ROLE_TENANT_ID", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_ROLE_TENANT_ID\", EnvironmentVariableTarget.Process)");
var clientId = Environment.GetEnvironmentVariable("AZ_ROLE_CLIENT_ID", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_ROLE_CLIENT_ID\", EnvironmentVariableTarget.Process)");
var clientSecret = Environment.GetEnvironmentVariable("AZ_ROLE_CLIENT_SECRET", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_ROLE_CLIENT_SECRET\", EnvironmentVariableTarget.Process)");

// Authenticate the arm client
var credentials = new ClientSecretCredential(
    System.Environment.GetEnvironmentVariable("AZ_ROLE_TENANT_ID", EnvironmentVariableTarget.Process),
    System.Environment.GetEnvironmentVariable("AZ_ROLE_CLIENT_ID", EnvironmentVariableTarget.Process),
    System.Environment.GetEnvironmentVariable("AZ_ROLE_CLIENT_SECRET", EnvironmentVariableTarget.Process)
);
ArmClient armClient = new ArmClient(credentials);
SubscriptionResource subscription = await armClient.GetDefaultSubscriptionAsync();
var resourceGroupResponse = await subscription.GetResourceGroupAsync("sdk-test");
var resourceGroup = resourceGroupResponse.Value;

AzureLocation location = AzureLocation.WestEurope;




// NetworkSecurityGroupCollection nsgCollection = resourceGroup.GetNetworkSecurityGroups();
// NetworkSecurityGroupData nsgData = new NetworkSecurityGroupData();
// nsgData.Location = location;
// var newNetworkSecurityGroup = await nsgCollection.CreateOrUpdateAsync(Azure.WaitUntil.Started, $"rpb-nsg-{accountId}", nsgData);

// Create network interface & attach existing IP address for account
NetworkInterfaceCollection niCollection = resourceGroup.GetNetworkInterfaces();
NetworkInterfaceData niData = new NetworkInterfaceData();
niData.Location = location;


// Get the IP from the remote performances resource group
PublicIPAddressCollection ipCollection = resourceGroup.GetPublicIPAddresses();
var publicIpAddressData = new PublicIPAddressData();
publicIpAddressData.Sku = new PublicIPAddressSku
{
    Tier = PublicIPAddressSkuTier.Regional,
    Name = PublicIPAddressSkuName.Standard
};
publicIpAddressData.Location = location;
publicIpAddressData.PublicIPAllocationMethod = NetworkIPAllocationMethod.Static;
var pipResponse = await ipCollection.CreateOrUpdateAsync(WaitUntil.Completed, $"remote-performances-pip-{accountId}", publicIpAddressData);
NetworkInterfaceIPConfigurationData ipData = new NetworkInterfaceIPConfigurationData();
ipData.Primary = true;
ipData.Id = pipResponse.Value.Id;
niData.IPConfigurations.Add(ipData);

var newNetworkInterface = await CreateVnet(resourceGroup, accountId, pipResponse.Value);
// var newNetworkInterface = await niCollection.CreateOrUpdateAsync(Azure.WaitUntil.Started, $"rpb-network-interface-{accountId}", niData);
Console.WriteLine($"NIC with ID {newNetworkInterface.Id} created");


// Create Virtual Machine
await CreateVm(resourceGroup, location, accountId, newNetworkInterface);


async Task<NetworkInterfaceResource> CreateVnet(ResourceGroupResource resourceGroupResource, string playerId, PublicIPAddressResource publicIpAddress)
{
    var subnetName = "mySubnet";
    var vnetData = new VirtualNetworkData
    {            Location = resourceGroupResource.Data.Location,

        AddressPrefixes = { "10.0.0.0/16" },
        Subnets =
        {
            new SubnetData
            {
                Name = subnetName,
                AddressPrefix = "10.0.0.0/28",
            }
        },
    };

    var virtualNetworkContainer = resourceGroupResource.GetVirtualNetworks();
    var vnet = await virtualNetworkContainer.CreateOrUpdate(WaitUntil.Completed, $"rpb-vnet-{playerId}", vnetData).WaitForCompletionAsync();
    // Console.WriteLine($"Created a Virtual Network {vnet.Value.Id}");

    // Console.WriteLine("Creating a Network Interface");
    var networkInterfaceData = new NetworkInterfaceData()
    {
        Location = resourceGroupResource.Data.Location,
        IPConfigurations = {
            new NetworkInterfaceIPConfigurationData
            {
                Name = "Primary",
                Primary = true,
                Subnet = new SubnetData() { Id = (await vnet.Value.GetSubnetAsync(subnetName)).Value.Id },
                // PrivateIPAllocationMethod = NetworkIPAllocationMethod.Static,
                PublicIPAddress = new PublicIPAddressData() { Id = publicIpAddress.Id }
            }
        }
    };

    var networkInterfaceContainer = resourceGroupResource.GetNetworkInterfaces();
    var networkInterface = await (await networkInterfaceContainer.CreateOrUpdateAsync(WaitUntil.Completed, $"rpb-nic-{playerId}", networkInterfaceData)).WaitForCompletionAsync();
    return networkInterface.Value;
}



async Task CreateVm(ResourceGroupResource resourceGroupResource, AzureLocation azureLocation, string playerId,
    NetworkInterfaceResource networkInterfaceResource)
{
    VirtualMachineCollection vmCollection = resourceGroupResource.GetVirtualMachines();
    VirtualMachineData vmData = new VirtualMachineData(azureLocation);
    vmData.Tags.Add("id", playerId);
    vmData.HardwareProfile = new VirtualMachineHardwareProfile();
    vmData.HardwareProfile.VmSize = new VirtualMachineSizeType("Standard_NC4as_T4_v3");
    vmData.StorageProfile = new VirtualMachineStorageProfile();
    vmData.StorageProfile.OSDisk = new VirtualMachineOSDisk("FromImage");
    vmData.StorageProfile.ImageReference = new ImageReference();
    vmData.StorageProfile.ImageReference.Offer = "WindowsServer";
    vmData.StorageProfile.ImageReference.Publisher = "MicrosoftWindowsServer";
    vmData.StorageProfile.ImageReference.Sku = "2022-Datacenter";
    vmData.StorageProfile.ImageReference.Version = "latest";
    vmData.StorageProfile.OSDisk.Name = $"Venue-RPB-OsDisk-{playerId}";
    vmData.StorageProfile.OSDisk.DiffDiskSettings = new DiffDiskSettings();
    vmData.StorageProfile.OSDisk.DiffDiskSettings.Option = "Local";
    vmData.StorageProfile.OSDisk.DiffDiskSettings.Placement = "ResourceDisk";
    vmData.StorageProfile.OSDisk.Caching = CachingType.ReadOnly;
    vmData.OSProfile = new VirtualMachineOSProfile();
    vmData.OSProfile.AdminUsername = "joebner";
    vmData.OSProfile.AdminPassword = Guid.NewGuid().ToString();
    vmData.OSProfile.ComputerName = "RPB";
    vmData.NetworkProfile = new VirtualMachineNetworkProfile();
    VirtualMachineNetworkInterfaceReference ni = new VirtualMachineNetworkInterfaceReference();
    ni.Id = new ResourceIdentifier(networkInterfaceResource.Id);
    vmData.NetworkProfile.NetworkInterfaces.Add(ni);
    vmData.UserData = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(playerId));
    var newVM = await vmCollection.CreateOrUpdateAsync(Azure.WaitUntil.Started, $"Venue-RPB-{playerId}", vmData);
}