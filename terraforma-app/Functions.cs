using Microsoft.Azure.WebJobs;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace terraform_app
{
    public class Functions
    {
        // This function will get triggered/executed when a new message is written 
        // on an Azure Queue called queue.
        public static void ProcessQueueMessage([QueueTrigger("")] string message, TextWriter log)
        {
            Log.Information(message);
            string timestamp = DateTime.Now.ToString("yyyyMMddHHss");
            string tempDirectory = Path.Combine(Environment.CurrentDirectory, $"terraform-{timestamp}");


            try
            {
                string scriptPah = Path.Combine(Environment.CurrentDirectory).ToString();
                var name = "PATH";
                var oldValue = Environment.GetEnvironmentVariable(name);
                if (!oldValue.Contains(scriptPah))
                {
                    var newValue = oldValue + ";" + scriptPah;
                    Environment.SetEnvironmentVariable(name, newValue);
                }

                // Define your variables
                string tenantId = "\"insert the tenant id here\"";
                string subscriptionId = "\"insert the subscription ID here\"";
                string clientId = "\"insert the Client ID here\"";
                string clientSecret = "\"insert the Client Secret here\"";

                // Create a dictionary of replacements
                Dictionary<string, string> replacements = new Dictionary<string, string>
                    {
                        { "var.tenant_id", tenantId },
                        { "var.subscription_id", subscriptionId },
                        { "var.client_id", clientId },
                        { "var.client_secret", clientSecret }
                    };

                if (message.EndsWith(".tf"))
                {
                    // Code to be executed if the message ends with .tf
                    var paramFileContent = new WebClient().DownloadString(message);
                    string scriptReplacedm = message.Replace("'", "\"");
                    Directory.CreateDirectory(tempDirectory);
                    string myTempFile = Path.Combine(tempDirectory, "main.tf");
                    FileStream fs = new FileStream(myTempFile, FileMode.CreateNew);
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(paramFileContent);
                    }
                    // Read the file content
                    string fileContent = File.ReadAllText(myTempFile);

                    // Use regex to replace the variables
                    foreach (var replacement in replacements)
                    {
                        fileContent = Regex.Replace(fileContent, Regex.Escape(replacement.Key), replacement.Value);
                    }

                    // Write the modified content back to the file
                    File.WriteAllText(myTempFile, fileContent);
                }
                else
                {
                    // Code to be executed if the message does not end with .tf
                    Directory.CreateDirectory(tempDirectory);
                    string source = message; // Assuming message contains the directory
                    string ssasToken = "Insert the Source Storage Account's SAS token here";

                    string azCopyDownloadCommand = $"azcopy copy \"{source}?{ssasToken}\" \"{tempDirectory}\" --recursive";

                    var azCopyDownloadStartInfo = new ProcessStartInfo()
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/C {azCopyDownloadCommand}",
                        UseShellExecute = false
                    };

                    Process.Start(azCopyDownloadStartInfo).WaitForExit();

                    // Extract the directory name from the message link
                    Uri uri = new Uri(message);
                    string directoryName = Path.GetFileName(uri.AbsolutePath);

                    // Path to the main.tf file in the directory
                    string mainTfFile = Path.Combine(tempDirectory, directoryName, "main.tf");

                    // Read the file content
                    string fileContent = File.ReadAllText(mainTfFile);

                    // Use regex to replace the variables
                    foreach (var replacement in replacements)
                    {
                        fileContent = Regex.Replace(fileContent, Regex.Escape(replacement.Key), replacement.Value);
                    }

                    // Write the modified content back to the file
                    File.WriteAllText(mainTfFile, fileContent);

                }

                int cloudPlatformId = 2; // Set the value of cloudPlatformId

                if (cloudPlatformId == 3)
                {
                    string keyUrl = "Insert the Key file's storage link";
                    string keyContent = new WebClient().DownloadString(keyUrl);
                    string keyFile = Path.Combine(tempDirectory, "key.json");
                    File.WriteAllText(keyFile, keyContent);
                }


                var loc = "Set-Location " + tempDirectory;
                string transcriptFileName = $"transcript-{timestamp}.txt";
                string transcriptFile = Path.Combine(tempDirectory, transcriptFileName);
                string startTranscript = $"Start-Transcript -Path {transcriptFile} -NoClobber -Append";
                string stopTranscript = "Stop-Transcript";

                string arg;

                if (message.EndsWith(".tf"))
                {
                    arg = string.Format("{0};{1};terraform init;terraform apply -auto-approve;{2}", loc, startTranscript, stopTranscript);
                }
                else
                {
                    // Extract the directory name from the message link
                    Uri uri = new Uri(message);
                    string directoryName = Path.GetFileName(uri.AbsolutePath);

                    arg = string.Format("{0};{1};cd {2};terraform init;terraform apply -auto-approve;{3}", loc, startTranscript, directoryName, stopTranscript);
                }

                var start = new ProcessStartInfo()
                {
                    Verb = "runas",
                    LoadUserProfile = true,
                    FileName = "powershell.exe",
                    Arguments = arg,
                    UseShellExecute = false,
                    RedirectStandardOutput = true, // Redirect the output stream
                    RedirectStandardError = true // Redirect the error stream
                };

                using (var process = Process.Start(start))
                {
                    // Start reading the output and error streams asynchronously
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    process.WaitForExit();

                    // Wait for the asynchronous read operations to complete
                    string output = outputTask.Result;
                    string error = errorTask.Result;

                    // Write the output and error streams to the transcript file
                    File.WriteAllText(transcriptFile, output);
                    File.AppendAllText(transcriptFile, error);
                }

                string storageAccountName = "Insert the storage account's name here for transcript logs";
                string containerName = "logs";
                string sasToken = "Insert the SAS token of the transcript logs storage account";
                string blobName = transcriptFileName;

                string azCopyCommand = $"azcopy copy \"{transcriptFile}\" \"https://{storageAccountName}.blob.core.windows.net/{containerName}/{transcriptFileName}?{sasToken}\"";


                var azCopyStartInfo = new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {azCopyCommand}",
                    UseShellExecute = false
                };

                Process.Start(azCopyStartInfo).WaitForExit();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
