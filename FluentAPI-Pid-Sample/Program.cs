using System;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Storage.Fluent;

namespace Fluent_Pid_Sample
{
    class Program
    {
        private const string ClientId = "INSERT-CLIENT-ID-HERE"; // Client/Application Id GUID from AAD application.
        private const string ClientKey = "INSERT-CLIENT-KEY-HERE"; // Client Key/Secret GUID from AAD application.
        private const string TenantId = "INSERT-TENANT-ID-HERE"; // AAD Tenant/Directory Id GUID.
        private const string SubscriptionId = "INSERT-SUBSCRIPTION-ID-HERE"; // Azure Subscription Id GUID.
        // Registered PID value, as described here: https://docs.microsoft.com/en-us/azure/marketplace/azure-partner-customer-usage-attribution
        private const string PidValue = "pid-00000000-0000-0000-0000-000000000000"; // Replace with registered PID value.

        static void Main(string[] args)
        {
            // SDK samples can be found here: https://github.com/Azure/azure-libraries-for-net
            Console.WriteLine("Fluent API PID Sample Started.");

            var credentials = new AzureCredentialsFactory().FromServicePrincipal(ClientId, ClientKey, TenantId, AzureEnvironment.AzureGlobalCloud);
            // Or, using MSI:
            // Option 1: Running inside a VM:
            //    var credentials = new AzureCredentialsFactory().FromMSI(new MSILoginInformation(MSIResourceType.VirtualMachine), AzureEnvironment.AzureGlobalCloud);
            // Option 2: Running inside an App Service or Function App:
            //    var credentials = new AzureCredentialsFactory().FromMSI(new MSILoginInformation(MSIResourceType.AppService), AzureEnvironment.AzureGlobalCloud);

            // *** Set the User Agent with the PID value: ***
            // The pid value should always be the last part of the user agent. The Customer Usage Attribution reporting that tracks it expects that and might not
            // reflect it correctly if added in the middle of a User Agent string.
            //
            // WithUserAgent() adds <product>/<version> to the request's User-Agent header.
            // To keep the pid value separate, do the following: .WithUserAgent("FluentAPI", "1.0 pid-00000000-0000-0000-0000-000000000000")
            // This turns into something similar to this (with the call adding the 'FluentAPI/1.0 pid-00000000-0000-0000-0000-000000000000' portion):
            //     User-Agent: XYZ/1.1 ABC/2.5 FluentAPI/1.0 pid-00000000-0000-0000-0000-000000000000
            //
            // NOTE: Beware that using this variation (with an empty product version) results in the PID not getting added at all:
            //    ***DO NOT USE*** .WithUserAgent("pid-00000000-0000-0000-0000-000000000000", "")
            IAzure azure = Azure.Configure()
                .WithUserAgent("FluentAPI", $"1.0 {PidValue}")
                .Authenticate(credentials)
                .WithSubscription(SubscriptionId);

            // This sample creates a new resource group and adds some resources to it in an Azure region (West US 2).
            int randomNumber = new Random((int)DateTime.Now.Ticks).Next(100000, 999999);
            var rgName = $"FluentAPISample-{randomNumber}-RG";

            var frontEndNSGName = $"{randomNumber}-NSG";
            var frontEndNSG = azure.NetworkSecurityGroups.Define(frontEndNSGName)
                .WithRegion(Region.USWest2)
                .WithNewResourceGroup(rgName)
                .DefineRule("ALLOW-SSH")
                    .AllowInbound()
                    .FromAnyAddress()
                    .FromAnyPort()
                    .ToAnyAddress()
                    .ToPort(22)
                    .WithProtocol(SecurityRuleProtocol.Tcp)
                    .WithPriority(100)
                    .WithDescription("Allow SSH")
                    .Attach()
                .DefineRule("ALLOW-HTTP")
                    .AllowInbound()
                    .FromAnyAddress()
                    .FromAnyPort()
                    .ToAnyAddress()
                    .ToPort(80)
                    .WithProtocol(SecurityRuleProtocol.Tcp)
                    .WithPriority(101)
                    .WithDescription("Allow HTTP")
                    .Attach()
                .Create();

            var vnetName = $"{randomNumber}-VNET";
            var vnet = azure.Networks.Define(vnetName)
                .WithRegion(Region.USWest2)
                .WithNewResourceGroup(rgName)
                .WithAddressSpace("10.0.0.0/28")
                .WithSubnet("subnet1", "10.0.0.0/29")
                .WithSubnet("subnet2", "10.0.0.8/29")
                .Create();

            var storageAccountName = $"{randomNumber}storage";
            var storageAccount = azure.StorageAccounts.Define(storageAccountName)
                .WithRegion(Region.USWest2)
                .WithNewResourceGroup(rgName)
                .WithSku(StorageAccountSkuType.Standard_LRS)
                .WithGeneralPurposeAccountKindV2()
                .Create();

            var vmDnsName = $"dns{randomNumber}";
            var nicName = $"{randomNumber}nic";
            var nic = azure.NetworkInterfaces.Define(nicName)
                .WithRegion(Region.USWest2)
                .WithExistingResourceGroup(rgName)
                .WithExistingPrimaryNetwork(vnet)
                .WithSubnet("subnet1")
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingNetworkSecurityGroup(frontEndNSG)
                .WithNewPrimaryPublicIPAddress(vmDnsName)
                .Create();

            var dataDiskName = $"{randomNumber}data";
            var dataDisk = azure.Disks.Define(dataDiskName)
                .WithRegion(Region.USWest2)
                .WithExistingResourceGroup(rgName)
                .WithData()
                .WithSizeInGB(127)
                .Create();

            var vmName = $"{randomNumber}vm";
            var windowsVM = azure.VirtualMachines.Define(vmName)
                .WithRegion(Region.USWest2)
                .WithExistingResourceGroup(rgName)
                .WithExistingPrimaryNetworkInterface(nic)
                .WithPopularWindowsImage(KnownWindowsVirtualMachineImage.WindowsServer2012R2Datacenter)
                .WithAdminUsername("jdoe")
                .WithAdminPassword("P2ssw0rd9999")
                .WithExistingDataDisk(dataDisk)
                .WithSize(VirtualMachineSizeTypes.StandardD2V2)
                .WithBootDiagnostics(storageAccount)
                .WithSystemAssignedManagedServiceIdentity()
                .Create();

            var snapshotName = $"{randomNumber}snapshot";
            var snapshot = azure.Snapshots.Define(snapshotName)
                .WithRegion(Region.USWest2)
                .WithExistingResourceGroup(rgName)
                .WithDataFromDisk(dataDisk)
                .Create();

            Console.WriteLine("Created a Windows VM: " + windowsVM.Id);
        }
    }
}
