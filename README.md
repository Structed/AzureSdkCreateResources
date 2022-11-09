# Azure SDK Create Resources Example
A small end-to-end example on how to create a VM with a public IP, and a NIC attached to a vnet/subnet using the new [Azure SDK for .NET](https://aka.ms/azsdk/net).

# Prerequisites
* [Azure Subscription](https://azure.microsoft.com/en-us/solutions/gaming/)
* [Azure Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal)
* [.NET 6](https://dot.net)

# Deploying
Please make sure to create an [Azure Service Principal](https://docs.microsoft.com/en-us/azure/active-directory/develop/howto-create-service-principal-portal).
This service principal needs to have create permissions on the resource group you created (you can configure the Resource Group name in the program)
## Environment Variables
> These are optional, but if you do not set them, you will be prompted by Terraform for their values.

| Name                          | Description                     |
|-------------------------------|---------------------------------|
| `AZ_NET_SDK_SAMPLE_TENANT_ID` | The Service Principal Tenant ID |
| `AZ_NET_SDK_SAMPLE_CLIENT_ID`           | The Service Principal Client ID |
| `AZ_NET_SDK_SAMPLE_SECRET`       | The Service Principal Secret    |