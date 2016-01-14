using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SharpCompress.Archive.SevenZip;
using SharpCompress.Archive.Zip;
using SharpCompress.Common;
using SharpCompress.Reader;

namespace SDInstaller
{
    class Program
    {
        private static string TempPath;
        private static string installLocation;
        private static string PluginManifestLocation;
        static void Main(string[] args)
        {
            if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.WriteLine("Usage: SDInstaller (SD API Key) (Tether web Location) (Tether install location) (OPTIONAL: Plugin Manifest Location)");
                return;
            }

            installLocation = args[2];            
            TempPath = Path.Combine(installLocation, "_temp");

            if (args.Length==4)
            {
                PluginManifestLocation = args[3];
            }

            if (!Directory.Exists(installLocation))
            {
                Directory.CreateDirectory(installLocation);
            }

            if (!Directory.Exists(TempPath))
            {
                Directory.CreateDirectory(TempPath);
            }

            WebClient client = new WebClient();
            Console.WriteLine("Downloading file");
            var localZip = Path.Combine(TempPath,  "Tether.zip");
            client.DownloadFile(args[1], localZip);
            Console.WriteLine("File Downloaded, executing");



            ServiceController ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ThreeOneThree.Tether");

            if (ctl == null)
            {
                Console.WriteLine("Performing Fresh install");
                PerformFreshInstall(args, localZip, client);
            }
            else
            {
                Console.WriteLine("Performing Upgrade");
                PerformUpgradeInstall(args, localZip, client, ctl);
            }

            
            Console.WriteLine("Finished");
        }

        private static void PerformUpgradeInstall(string[] args, string localZip, WebClient client, ServiceController ctl)
        {
            StopService(ctl);
            StopService(ctl);
            StopService(ctl); // Just to be sure it's not running!

            var settingsFile = Path.Combine(installLocation, "settings.json");
            var tempSettings = Path.Combine(TempPath, "settings.json");
            var copySettingsBack = false;

            if (File.Exists(settingsFile))
            {
                File.Copy(settingsFile, tempSettings, true);
                copySettingsBack = true;
            }

            ExtractFilesToLocation(localZip, installLocation);

            File.Delete(Path.Combine(TempPath, "Tether.zip"));

            File.WriteAllText( Path.Combine(installLocation, "tether.exe.config")  ,File.ReadAllText(Path.Combine(installLocation, "tether.exe.config")).Replace("Trace", "Error"));

            if (copySettingsBack)
            {
                File.Move(tempSettings, settingsFile);
            }

            Thread.Sleep(TimeSpan.FromSeconds(15));

            ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ThreeOneThree.Tether");
            ctl.Start();
            ctl.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            if (ctl.Status != ServiceControllerStatus.Running)
            {
                Console.WriteLine("Not running!");
            }
        }

        private static void StopService(ServiceController ctl)
        {
            if (ctl.Status != ServiceControllerStatus.Stopped)
            {
                ctl.Stop();
                ctl.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }

        private static void PerformFreshInstall(string[] args, string localZip, WebClient client)
        {
            ExtractFilesToLocation(localZip, installLocation);

            File.Delete(Path.Combine(TempPath, "Tether.zip"));

            string SDKey = args[0];

            var downloadString = client.DownloadString(@"https://api.serverdensity.io/inventory/resources?token=" + SDKey + @"&filter={""name"":""" + Environment.MachineName.ToLower() + @""",""type"":""device""}&fields=[""agentKey""]");

            var jObject = JArray.Parse(downloadString);

            string AccountName = GetAccountName(client, SDKey);

            if (String.IsNullOrWhiteSpace(AccountName))
            {
                Console.WriteLine("! Unable to get account name !");
            }

            string AgentKey;

            if (jObject.Any())
            {
                Console.WriteLine("Agent Key Found!");

                AgentKey = jObject.FirstOrDefault<dynamic>().agentKey;

                Console.WriteLine("Agent key is " + AgentKey);
            }
            else
            {
                Console.WriteLine("Creating Agent Key");

                string createMachineRequest = JObject.FromObject(new {name = Environment.MachineName.ToLower()}).ToString();

                client.Headers.Set(HttpRequestHeader.ContentType, "application/json");

                string response = client.UploadString("https://api.serverdensity.io/inventory/devices?token=" + SDKey, createMachineRequest);

                AgentKey = JObject.Parse(response)["agentKey"].ToString();

                Console.WriteLine("Agent key is " + AgentKey);
            }

            var configLocation = Path.Combine(installLocation, "settings.json");

            if (!File.Exists(configLocation))
            {
                throw new Exception("Config file does not exist!");
            }

            string config =
                @"{
    ""ServerDensityUrl"": ""https://" + AccountName + @".serverdensity.io"",
    ""ServerDensityKey"": """ + AgentKey + @""",
    ""CheckInterval"": 60,
    ""PluginManifestLocation"": """ + PluginManifestLocation + @"""
}";

            File.WriteAllText(configLocation, config);

            File.WriteAllText(Path.Combine(installLocation, "tether.exe.config"), File.ReadAllText(Path.Combine(installLocation, "tether.exe.config")).Replace("Trace", "Error"));

            Process.Start( Path.Combine(installLocation, "Tether.exe"), "install");

            Process.Start("net", "start \"ThreeOneThree.Tether\"").WaitForExit();

            Console.WriteLine("Service Started");
        }

        private static void ExtractFilesToLocation(string fileName, string Path)
        {
            if (!Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
            }

            using (var sevenZipArchive = ZipArchive.Open(fileName))
            {
                using (var reader = sevenZipArchive.ExtractAllEntries())
                {
                    reader.WriteAllToDirectory(Path, ExtractOptions.ExtractFullPath | ExtractOptions.Overwrite);
                }
            }
        }
        public static string GetAccountName(WebClient client, string SDKey)
        {
            var downloadString = client.DownloadString("https://api.serverdensity.io/users/users?token=" + SDKey);

            var jObject = JArray.Parse(downloadString);

            foreach (dynamic jToken in jObject.Cast<dynamic>().Where(jToken => jToken.accountName != null && !string.IsNullOrWhiteSpace(jToken.accountName.ToString()))) {
                return jToken.accountName;
            }

            return "";
        }
    }
}
