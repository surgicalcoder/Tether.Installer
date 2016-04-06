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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PowerArgs;
using SharpCompress.Archive.SevenZip;
using SharpCompress.Archive.Zip;
using SharpCompress.Common;
using SharpCompress.Reader;

namespace SDInstaller
{

    public class InstallArgs
    {
        [ArgPosition(0)]
        [ArgRequired()]
        [ArgShortcut("API")]
        public string SDAPIKey { get; set; }

        [ArgPosition(1)]
        [ArgRequired()]
        [ArgShortcut(ArgShortcutPolicy.NoShortcut)]
        public string TetherLocation { get; set; }

        [ArgPosition(2)]
        [ArgRequired()]
        [ArgShortcut("InstallTo")]
        public string InstallLocation { get; set; }

        [ArgShortcut("Manifest")]
        public string ManifestLocation { get; set; }

        [ArgDefaultValue("https://{account}.agent.serverdensity.io ")]
        [ArgShortcut("PostLocation")]
        public string ServerDensityPostLocation { get; set; }

        public string TempPath { get; set; }

        [ArgDefaultValue(true)]
        [ArgShortcut(ArgShortcutPolicy.NoShortcut)]
        public bool PreserveConfigFile { get; set; }


        [ArgShortcut(ArgShortcutPolicy.NoShortcut)]
        public string SDAccountName { get; set; }

        [ArgShortcut(ArgShortcutPolicy.NoShortcut)]
        public string SDAgentKey { get; set; }

    }


    class Program
    {
        private static InstallArgs options;

        static void Main(string[] args)
        {
            try
            {
                options = Args.Parse<InstallArgs>(args);
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ArgUsage.GenerateUsageFromTemplate<InstallArgs>());
                return;
            }

            try
            {
                if (String.IsNullOrWhiteSpace(options.TempPath))
                {
                    options.TempPath = Path.Combine(options.InstallLocation, "_temp");
                }

                if (!Directory.Exists(options.InstallLocation))
                {
                    Directory.CreateDirectory(options.InstallLocation);
                }

                if (!Directory.Exists(options.TempPath))
                {
                    Directory.CreateDirectory(options.TempPath);
                }

                WebClient client = new WebClient();
                Console.WriteLine("Downloading file");
                var localZip = Path.Combine(options.TempPath,  "Tether.zip");
                client.DownloadFile(options.TetherLocation, localZip);
                Console.WriteLine("File Downloaded, executing");
                
                ServiceController ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ThreeOneThree.Tether");

                if (ctl == null)
                {
                    Console.WriteLine("Performing Fresh install");
                    PerformFreshInstall(localZip, client);
                }
                else
                {
                    Console.WriteLine("Performing Upgrade");
                    PerformUpgradeInstall(localZip, client, ctl);
                }

                Console.WriteLine("Finished");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void PerformUpgradeInstall(string localZip, WebClient client, ServiceController ctl)
        {
            StopService(ctl);
            StopService(ctl);
            StopService(ctl); // Just to be sure it's not running!

            var settingsFile = Path.Combine(options.InstallLocation, "settings.json");
            var tempSettings = Path.Combine(options.TempPath, "settings.json");

            var copySettingsBack = false;

            if (File.Exists(settingsFile) && options.PreserveConfigFile)
            {
                File.Copy(settingsFile, tempSettings, true);
                copySettingsBack = true;
            }

            ExtractFilesToLocation(localZip, options.InstallLocation);

            File.Delete(Path.Combine(options.TempPath, "Tether.zip"));

            File.WriteAllText( Path.Combine(options.InstallLocation, "tether.exe.config")  ,File.ReadAllText(Path.Combine(options.InstallLocation, "tether.exe.config")).Replace("Trace", "Error"));

            if (copySettingsBack)
            {
                if (File.Exists(settingsFile))
                {
                    File.Delete(settingsFile);
                }
                File.Move(tempSettings, settingsFile);
            }
            else
            {
                GenerateConfiguration(options.SDAccountName ?? GetAccountName(client, options.SDAPIKey), GetAgentKey());
            }

            var pluginLocation = Path.Combine(options.InstallLocation, "plugins");

            if (Directory.Exists(pluginLocation) && Directory.GetFiles(pluginLocation, "Tether.CoreChecks.*").Any())
            {
                foreach (var file in Directory.GetFiles(pluginLocation, "Tether.CoreChecks.*")) // This file was accidentally copied from a few builds, so lets make sure they don't exist!
                {
                    File.Delete(file);
                }
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

        private static void PerformFreshInstall(string localZip, WebClient client)
        {
            ExtractFilesToLocation(localZip, options.InstallLocation);

            File.Delete(Path.Combine(options.TempPath, "Tether.zip"));

            string AccountName = options.SDAccountName ?? GetAccountName(client, options.SDAPIKey);

            if (String.IsNullOrWhiteSpace(AccountName))
            {
                Console.WriteLine("! Unable to get account name !");
            }

            var AgentKey = GetAgentKey();

            GenerateConfiguration(AccountName, AgentKey);

            File.WriteAllText(Path.Combine(options.InstallLocation, "tether.exe.config"), File.ReadAllText(Path.Combine(options.InstallLocation, "tether.exe.config")).Replace("Trace", "Error"));

            Process.Start( Path.Combine(options.InstallLocation, "Tether.exe"), "install").WaitForExit();

            Process.Start("net", "start \"ThreeOneThree.Tether\"").WaitForExit();

            Console.WriteLine("Service Started");
        }

        private static string GetAgentKey()
        {
            if (!String.IsNullOrWhiteSpace(options.SDAgentKey))
            {
                return options.SDAgentKey;
            }

            string AgentKey;
            using (WebClient client = new WebClient()) {
                var downloadString = client.DownloadString(@"https://api.serverdensity.io/inventory/resources?token=" + options.SDAPIKey + @"&filter={""name"":""" + Environment.MachineName.ToLower() + @""",""type"":""device""}&fields=[""agentKey""]");

                var jObject = JArray.Parse(downloadString);

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

                    string response = client.UploadString("https://api.serverdensity.io/inventory/devices?token=" + options.SDAPIKey, createMachineRequest);

                    AgentKey = JObject.Parse(response)["agentKey"].ToString();

                    Console.WriteLine("Agent key is " + AgentKey);
                }
            }
            return AgentKey;
        }

        private static void GenerateConfiguration(string AccountName, string AgentKey)
        {
            var configLocation = Path.Combine(options.InstallLocation, "settings.json");

            TetherAgentConfig agentConfig = new TetherAgentConfig
            {
                CheckInterval = 60,
                ServerDensityUrl = options.ServerDensityPostLocation.Replace("{account}", AccountName),
                ServerDensityKey = AgentKey,
                PluginManifestLocation = options.ManifestLocation
            };


            File.WriteAllText(configLocation, JsonConvert.SerializeObject(agentConfig));
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
        private static string GetAccountName(WebClient client, string SDKey)
        {
            if (!String.IsNullOrWhiteSpace(options.SDAccountName))
            {
                return options.SDAccountName;
            }

            var downloadString = client.DownloadString("https://api.serverdensity.io/users/users?token=" + SDKey);

            var jObject = JArray.Parse(downloadString);

            foreach (dynamic jToken in jObject.Cast<dynamic>().Where(jToken => jToken.accountName != null && !string.IsNullOrWhiteSpace(jToken.accountName.ToString()))) {
                return jToken.accountName;
            }

            return "";
        }
    }

    public class TetherAgentConfig
    {
        public string ServerDensityUrl { get; set; }
        public string ServerDensityKey { get; set; }
        public int CheckInterval { get; set; }
        public string PluginManifestLocation { get; set; }
    }
}
