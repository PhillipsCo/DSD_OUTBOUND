using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using Serilog;

namespace DSD_Outbound.Services
{
    internal class FtpService
    {
        void ftpFiles(String ftpHost, String ftpUser, String ftpPass, String ftpRemoteFilePath, String ftpLocalFilePath)
        {
            using (var client = new SftpClient(ftpHost, ftpUser, ftpPass))
            {
                try
                {
                    client.Connect();
                    Console.WriteLine("CONNECTED TO CIS FTP");
                    Log.Information("CONNECTED TO CIS FTP");
                    //addToContent("CONNECTED TO CIS FTP");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Connection Failed " + ex.Message);
                    Log.Information("Connection Failed " + ex.Message);

                    Environment.Exit(1);
                }
                Stopwatch sw = new Stopwatch();
                sw.Start();
                while (sw.Elapsed.TotalSeconds < 300)
                {
                    if (!client.Exists(ftpRemoteFilePath + "WaitERP"))
                    {
                        break;
                    }
                    //Console.WriteLine(sw.Elapsed.TotalSeconds);
                }
                if (sw.Elapsed.TotalSeconds > 300)
                {
                    Console.WriteLine("unable to upload FTP Busy");
                    Log.Information("unable to upload FTP Busy");
                    Environment.Exit(1);

                }
                sw.Stop();

                var fileStream = new FileStream("c:/CIS/WaitCIS", FileMode.Open);
                client.UploadFile(fileStream, ftpRemoteFilePath + "WaitCIS");
                client.UploadFile(fileStream, ftpRemoteFilePath.Replace("MasterData", "Orders") + "WaitCIS");
                fileStream.Close();
                try
                {
                    var files = from file in Directory.EnumerateFiles(ftpLocalFilePath + "\\Outbound\\" + DateTime.Now.ToString("yyyyMMdd")) select file;

                    foreach (var file in files)
                    {
                        if (file.EndsWith(".csv"))
                        {
                            String fileName = Path.GetFileName(file);
                            String RemoteFile = ftpRemoteFilePath + Path.GetFileName(file);
                            if (fileName.StartsWith("ORD"))
                                RemoteFile = ftpRemoteFilePath.Replace("MasterData", "Orders") + Path.GetFileName(file);
                            var fs = new FileStream(file, FileMode.Open);
                            client.UploadFile(fs, RemoteFile);
                            Console.WriteLine(Path.GetFileName(file) + " Loaded");
                            Log.Information(Path.GetFileName(file) + " Loaded");
                            //addToContent(Path.GetFileName(file) + " Loaded");

                        }
                    }
                    client.Delete(ftpRemoteFilePath + "WaitCIS");
                    client.Delete(ftpRemoteFilePath.Replace("MasterData", "Orders") + "WaitCIS");
                    var fileStream2 = new FileStream("c:/CIS/ReadyCIS", FileMode.Open);
                    client.UploadFile(fileStream2, ftpRemoteFilePath + "ReadyCIS");
                    client.UploadFile(fileStream2, ftpRemoteFilePath.Replace("MasterData", "Orders") + "ReadyCIS");
                }
                catch (Exception ex) { Console.WriteLine("Uploading Error " + ex.Message); }
            }

        }
    }
}
