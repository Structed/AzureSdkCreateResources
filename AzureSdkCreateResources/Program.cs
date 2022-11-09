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

// CONFIG STUFF
const string resourceNamePrefix = "sdkt";   // This Resource Group has to exist before executing, including proper permissions for the Service Principal to create resources in this Resource Group
const string baseResourceGroup = "sdk-test";
const string subnetName = $"{resourceNamePrefix}-subnet";
const string vmSku = "Standard_NC4as_T4_v3";    // Make sure you have quota! https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits
string vmAdminPassword = Guid.NewGuid().ToString();
string accountId = Guid.NewGuid().ToString();
AzureLocation location = AzureLocation.WestEurope;




var tenantId = Environment.GetEnvironmentVariable("AZ_NET_SDK_TENANT_ID", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_NET_SDK_TENANT_ID\", EnvironmentVariableTarget.Process)");
var clientId = Environment.GetEnvironmentVariable("AZ_NET_SDK_CLIENT_ID", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_NET_SDK_CLIENT_ID\", EnvironmentVariableTarget.Process)");
var clientSecret = Environment.GetEnvironmentVariable("AZ_NET_SDK_CLIENT_SECRET", EnvironmentVariableTarget.Process) ?? throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"AZ_NET_SDK_CLIENT_SECRET\", EnvironmentVariableTarget.Process)");

// Authenticate the ARM client
var credentials = new ClientSecretCredential(
    tenantId,
    clientId,
    clientSecret
);

ArmClient armClient = new ArmClient(credentials);
SubscriptionResource subscription = await armClient.GetDefaultSubscriptionAsync();
var resourceGroupResponse = await subscription.GetResourceGroupAsync(baseResourceGroup);
var resourceGroup = resourceGroupResponse.Value;



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
var pipResponse = await ipCollection.CreateOrUpdateAsync(WaitUntil.Completed, $"{resourceNamePrefix}-pip-{accountId}", publicIpAddressData);
NetworkInterfaceIPConfigurationData ipData = new NetworkInterfaceIPConfigurationData();
ipData.Primary = true;
ipData.Id = pipResponse.Value.Id;
niData.IPConfigurations.Add(ipData);

var newNetworkInterface = await CreateVnet(resourceGroup, accountId, pipResponse.Value, subnetName);
Console.WriteLine($"NIC with ID {newNetworkInterface.Id} created");


// Create Virtual Machine
var vmCreationResult = await CreateVm(resourceGroup, location, accountId, newNetworkInterface, vmSku, vmAdminPassword);
Console.WriteLine($"VM {vmCreationResult.Value.Data.Name} created");


async Task<NetworkInterfaceResource> CreateVnet(ResourceGroupResource resourceGroupResource, string playerId, PublicIPAddressResource publicIpAddress, string subnetName)
{
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
    var vnet = await virtualNetworkContainer.CreateOrUpdate(WaitUntil.Completed, $"{resourceNamePrefix}-vnet-{playerId}", vnetData).WaitForCompletionAsync();
    var networkInterfaceData = new NetworkInterfaceData()
    {
        Location = resourceGroupResource.Data.Location,
        IPConfigurations = {
            new NetworkInterfaceIPConfigurationData
            {
                Name = "Primary",
                Primary = true,
                Subnet = new SubnetData() { Id = (await vnet.Value.GetSubnetAsync(subnetName)).Value.Id },
                PublicIPAddress = new PublicIPAddressData() { Id = publicIpAddress.Id }
            }
        }
    };

    var networkInterfaceContainer = resourceGroupResource.GetNetworkInterfaces();
    var networkInterface = await (await networkInterfaceContainer.CreateOrUpdateAsync(WaitUntil.Completed, $"{resourceNamePrefix}-nic-{playerId}", networkInterfaceData)).WaitForCompletionAsync();
    return networkInterface.Value;
}



async Task<ArmOperation<VirtualMachineResource>> CreateVm(ResourceGroupResource resourceGroupResource, AzureLocation azureLocation, string playerId, NetworkInterfaceResource networkInterfaceResource, string sku, string password)
{
    VirtualMachineCollection vmCollection = resourceGroupResource.GetVirtualMachines();
    VirtualMachineData vmData = new VirtualMachineData(azureLocation);
    vmData.Tags.Add("id", playerId);
    vmData.HardwareProfile = new VirtualMachineHardwareProfile();
    vmData.HardwareProfile.VmSize = new VirtualMachineSizeType(sku);
    vmData.StorageProfile = new VirtualMachineStorageProfile();
    vmData.StorageProfile.OSDisk = new VirtualMachineOSDisk("FromImage");
    vmData.StorageProfile.ImageReference = new ImageReference();
    vmData.StorageProfile.ImageReference.Offer = "WindowsServer";
    vmData.StorageProfile.ImageReference.Publisher = "MicrosoftWindowsServer";
    vmData.StorageProfile.ImageReference.Sku = "2022-Datacenter";
    vmData.StorageProfile.ImageReference.Version = "latest";
    vmData.StorageProfile.OSDisk.Name = $"{resourceNamePrefix}-OsDisk-{playerId}";
    vmData.StorageProfile.OSDisk.DiffDiskSettings = new DiffDiskSettings();
    vmData.StorageProfile.OSDisk.DiffDiskSettings.Option = "Local";
    vmData.StorageProfile.OSDisk.DiffDiskSettings.Placement = "ResourceDisk";
    vmData.StorageProfile.OSDisk.Caching = CachingType.ReadOnly;
    vmData.OSProfile = new VirtualMachineOSProfile();
    vmData.OSProfile.AdminUsername = "superuser";
    vmData.OSProfile.AdminPassword = password;
    vmData.OSProfile.ComputerName = $"{resourceNamePrefix}vm";
    vmData.NetworkProfile = new VirtualMachineNetworkProfile();
    VirtualMachineNetworkInterfaceReference ni = new VirtualMachineNetworkInterfaceReference();
    ni.Id = new ResourceIdentifier(networkInterfaceResource.Id);
    vmData.NetworkProfile.NetworkInterfaces.Add(ni);
    vmData.UserData = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(playerId));
    var newVm = await vmCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, $"{resourceNamePrefix}-vm-{playerId}", vmData);
    return newVm;
}