// See https://aka.ms/new-console-template for more information
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Mail;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Azure.Identity;
using Azure.Identity;
using BCoutbound;
using FluentFTP;
using ftpTesting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Cms;
using Renci.SshNet;
using Renci.SshNet.Messages;
using RestSharp;
using Serilog;
using static Microsoft.Graph.Constants;
using Message = Microsoft.Graph.Models.Message;




string content = "<p>";
string status = "OK";
Log.Logger = new LoggerConfiguration()
       .WriteTo.Console()
       .WriteTo.File($"C:\\CIS\\logs\\OutBound {args[0]} log-.txt", rollingInterval: RollingInterval.Day)
       .CreateLogger();

try
{
    
    //CreateHostBuilder(args).Build().Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed");
}
finally
{
    //Log.CloseAndFlush();
}


try
{
    Log.Information (args[0] + " Begin Inbound to CIS" );
     addToContent(args[0] + " Begin Inbound to CIS");
    List<string> custs = new List<string> { "DEMO", "MOR", "LEI","MOR2" };
    int idx = custs.IndexOf(args[0]);
    if (args.Length == 0)
    {
        Log.Information("Missing Argument");
        content += "Missing Argument\n\r";
    }
    if (idx == -1)
    {
        Log.Information("Invalid Argument " + args[0]);
        content += "Invalid Argument\n\r";
    }
    Console.WriteLine("Customer = " + args[0]);
}
catch (Exception ex) 
{
    Log.Information("Error Message = " + ex.Message.ToString());
    Environment.Exit(0);
}

// GET ACCESS TOKEN using info in talend test db
string SQL = "select * from DSD_CustomerInfo where customer = '" + args[0] + "'";

SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

buildConnection(builder, ConfigurationManager.AppSettings["DataSource"], ConfigurationManager.AppSettings["UserID"],
    ConfigurationManager.AppSettings["Password"], ConfigurationManager.AppSettings["InitialCatalog"]);

var accessInfo = new AccessInfo();
try
{
    using (SqlConnection conn = new SqlConnection(builder.ConnectionString))
    {
        conn.Open();
        Log.Information($"Conected to: {conn.Database}");
        SqlCommand AccessInfoCommand = new SqlCommand(SQL, conn);
        loadAccessInfo(AccessInfoCommand, accessInfo);
        //Console.WriteLine(accessInfo.ftpPass);
        conn.Close();
    }
}
catch (Exception ex)
{
    Log.Information($"Error Connect to db: {ex.Message}");
    await sendEmail(accessInfo.email_tenantId, accessInfo.email_clientId, accessInfo.email_secret, accessInfo.email_sender, accessInfo.email_recipient.Split(',').ToList(), $"Failure connecting to customerconnect", ex.Message);
}

var tokenInfo = new TokenInfo();
var client = new RestClient();
var request = new RestRequest(accessInfo.Url, Method.Post);

request.AlwaysMultipartFormData = true;
request.AddParameter("Grant_Type", accessInfo.Grant_Type);
request.AddParameter("Client_ID", accessInfo.Client_ID);
request.AddParameter("Client_Secret", accessInfo.Client_Secret);
request.AddParameter("Scope", accessInfo.Scope);
RestResponse response = await client.ExecuteAsync(request);
tokenInfo = JsonConvert.DeserializeObject<TokenInfo>(response.Content);

SqlConnectionStringBuilder custBuilder = new SqlConnectionStringBuilder();
//Customer SQL Connection
buildConnection(custBuilder, accessInfo.DataSource, accessInfo.UserID, accessInfo.Password, accessInfo.InitialCatalog);
//Get API List and process
SQL = "Select [TABLE_NAME], [API_NAME],filter,batchsize from  dsd_api_list Where Dir = 'Outbound' Order By api_name";
string subject = "Test Email from Azure App";
List<TableApiName> tableApiNames = new List<TableApiName>();
using (SqlConnection conn = new SqlConnection(custBuilder.ConnectionString))
{
    conn.Open();
    Log.Information($"Conected to: {conn.Database} getting API List");
    SqlCommand APIListcommand = new SqlCommand(SQL, conn);
    using (SqlConnection conndel = new SqlConnection(custBuilder.ConnectionString))
    {
        conndel.Open();
        using (SqlDataReader rdr = APIListcommand.ExecuteReader())

        {
            while (rdr.Read())
            {
                TableApiName x = new TableApiName();
                x.tableName = rdr["TABLE_NAME"].ToString();
                x.APIname = rdr["API_NAME"].ToString();
                x.filter = rdr["FILTER"].ToString();
                x.batchSize = (int)rdr["BATCHSIZE"];

                tableApiNames.Add(x);
                //Truncate all HFS Tables
                SqlCommand commanddel = new SqlCommand("TRUNCATE TABLE " + x.tableName, conndel);
                commanddel.ExecuteNonQuery();
            }
            rdr.Close();

        }
        conn.Close();
        conndel.Close();


    }
    
}

//sendEmail(accessInfo.email_tenantId, accessInfo.email_clientId, accessInfo.email_secret, accessInfo.email_sender, accessInfo.email_recipient, subject, content);
foreach (TableApiName APIlist in tableApiNames)
{
   
    Console.WriteLine(APIlist.APIname);
    var json = await CallApiAsync(accessInfo.RootUrl + APIlist.APIname, tokenInfo.access_token, APIlist.APIname, APIlist.filter, (int)APIlist.batchSize, APIlist.tableName);


}
//if (args[0] =="LEI")
//    HFSCustomerCard_XML();

List<CSV> tbls = new List<CSV>();

SqlCommand CSVcommand = null;
using (SqlConnection conn = new SqlConnection(custBuilder.ConnectionString))
{
    conn.Open();
    SQL = "SELECT TABLE_NAME FROM  INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME like 'CIS_%'  AND TABLE_NAME NOT like 'CISOUT_%' GROUP BY TABLE_NAME";
    CSVcommand = new SqlCommand(SQL, conn);
    using (SqlDataReader rdr = CSVcommand.ExecuteReader())
    {
        while (rdr.Read())
        {
            CSV tbl = new CSV();
            tbl.tableName = (string)rdr["TABLE_NAME"];
            tbls.Add(tbl);
        }
    }
    foreach (CSV tbl in tbls)
    {
        SQL = "SELECT * FROM  " + tbl.tableName;
        CSVcommand = new SqlCommand(SQL, conn);

        using (SqlDataReader rdr = CSVcommand.ExecuteReader())
        {
            List<string> recs = new List<string>();
            while (rdr.Read())
            {
                object[] values = new object[rdr.FieldCount];
                rdr.GetValues(values);
                recs.Add(string.Join("|", values));
            }
            DirectoryInfo di = Directory.CreateDirectory(accessInfo.ftpLocalFilePath + "\\" + DateTime.Now.ToString("yyyyMMdd"));
            File.WriteAllLines(accessInfo.ftpLocalFilePath + "\\" + DateTime.Now.ToString("yyyyMMdd") + "\\" + tbl.tableName.Replace("CIS_","") + ".csv", recs);
        }
    }
}
//Log.Information("Outbound Complete");
//addToContent( "Outbound Complete");

subject =  $"DSD {args[0]} data sent to CIS Status of run = {status}";


ftpFiles(accessInfo.ftpHost, accessInfo.ftpUser, accessInfo.ftpPass, accessInfo.ftpRemoteFilePath + "Inbound/MasterData/", accessInfo.ftpLocalFilePath);
Log.Information(args[0] + " Outbound to CIS complete"  );
content = content + "</p>";
await sendEmail(accessInfo.email_tenantId, accessInfo.email_clientId, accessInfo.email_secret, accessInfo.email_sender, accessInfo.email_recipient.Split(',').ToList(), subject, content);
Log.CloseAndFlush();



void ftpFiles(String ftpHost, String ftpUser, String ftpPass, String ftpRemoteFilePath, String ftpLocalFilePath)
{
    using (var client = new SftpClient(ftpHost, ftpUser, ftpPass))
    {
        try
        {
            client.Connect();
            Console.WriteLine("CONNECTED TO CIS FTP");
            Log.Information("CONNECTED TO CIS FTP");
            addToContent("CONNECTED TO CIS FTP");   
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
            if (!client.Exists(ftpRemoteFilePath  + "WaitERP"))
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
            var files = from file in Directory.EnumerateFiles(ftpLocalFilePath + "\\" + DateTime.Now.ToString("yyyyMMdd")) select file;

            foreach (var file in files)
            {
                if (file.EndsWith(".csv"))
                {
                    String fileName = Path.GetFileName(file);
                    String RemoteFile = ftpRemoteFilePath + Path.GetFileName(file);
                    if(fileName.StartsWith("ORD"))
                        RemoteFile = ftpRemoteFilePath.Replace("MasterData","Orders") + Path.GetFileName(file);
                    var fs = new FileStream(file, FileMode.Open);
                    client.UploadFile(fs, RemoteFile);
                    Console.WriteLine(Path.GetFileName(file) + " Loaded");
                    Log.Information(Path.GetFileName(file) + " Loaded");
                   addToContent( Path.GetFileName(file) + " Loaded");

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
 
 async Task sendEmail(string tenantId, string clientId, string clientSecret, string senderEmail, List<string> recipientEmails, string subject,string content)
{
    try
    {
        // Authenticate using client credentials

        var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(clientSecretCredential);

        // Build recipient list
        var toRecipients = recipientEmails.Select(email => new Recipient
        {
            EmailAddress = new EmailAddress
            {
                Address = email
            }
        }).ToList();

        // Create email message
        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = content
             },
            ToRecipients = toRecipients
        };

        // Send email
        await graphClient.Users[senderEmail]
            .SendMail
            .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });

        //await graphClient.Users["cody.phillips@harvestfoodsolutions.com"].SendMail(message, false).Request.PostAsync;
        ////.SendMail(message, false).Request().PostAsync();
        Log.Information("Email sent successfully!");
        Console.WriteLine("Email sent successfully!");
        
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending email: {ex.Message}");
        Log.Information($"Error sending email: {ex.Message}");

    }
}


async Task<string> CallApiAsync(string Url, string AccessToken, string APIName,string criteria, int batch,string tableName)
{
   
    using HttpClient client = new HttpClient();

    int skip = 0;
    int batchSize = batch;
    bool moreRecords = true;
    int recordCount = 0;
    if (criteria != "N")
    {
        criteria = criteria.Replace("SHIPDATE", DateTime.Now.AddDays(Convert.ToInt32(accessInfo.DayOffset)).ToString("yyyy-MM-dd")).Replace("ENDDATE", DateTime.Now.AddDays(Convert.ToInt32(accessInfo.DayOffset) + 7).ToString("yyyy-MM-dd"));
        string dow = DateTime.Today.AddDays(Convert.ToInt32(accessInfo.DayOffset)).DayOfWeek.ToString();
        criteria = criteria.Replace("xxxdowxxx", dow);
        DateTime orderdt = GetDateBasedOn1300();
        criteria = criteria.Replace("xxxorderdatexxx", orderdt.ToString("yyyy-MM-dd"));

    }
    else
    {
        criteria = string.Empty;
    }

        while (moreRecords)
        {
            try
            {
                string Records = "";
                string filter = $"?$top={batchSize}&$skip={skip}";
                filter = filter + criteria;
                //Console.WriteLine(filter);
                var url = Url + filter;
            //Console.WriteLine(url);
           


            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
             
            var response = await client.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();

                var jObject = JObject.Parse(json);
                var records = jObject["value"];

                int count = records?.Count() ?? 0;
                recordCount = count + recordCount;

               
                response.EnsureSuccessStatusCode();
                Console.WriteLine($"Status Code: {response.StatusCode} for loading {tableName} Skipping {skip}");
                     Log.Information($"Status Code: {response.StatusCode} for loading {tableName} Skipping {skip}");
            //addToContent($"Status Code: {response.StatusCode} for loading {tableName} Skipping {skip}");
                if (count == 0)
                {
                    moreRecords = false;
                Log.Information($"total records from {tableName} = {recordCount}");
                Console.WriteLine($"total records from {tableName} = {recordCount}");
                addToContent($"total records from {tableName} = {recordCount}");
            }
                else
                {
                    
                    json = json.Split('[', ']')[1];
                    json = json.Replace("'", "''");
                    json = "[" + json + "]";
                    var dataTable = JsonConvert.DeserializeObject<DataTable>(json);
                    //dataTable.Columns.RemoveAt(0);
                //int xx = dataTable.Columns.Count;
                    using (SqlConnection conn = new SqlConnection(custBuilder.ConnectionString))
                    {
                    conn.Open();
                    Log.Information($"Loading HFS Tables in : {conn.Database}");
                    List<ColumnList> columnLists = new List<ColumnList>();
                    SQL = "SELECT COLUMN_NAME,CAST(CHARACTER_MAXIMUM_LENGTH AS VARCHAR) AS CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS   WHERE TABLE_NAME = N'" + tableName + "'";
                    SqlCommand command = new SqlCommand(SQL, conn);
                    using (SqlDataReader rdr = command.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            ColumnList cl = new ColumnList();
                            cl.colName = (string)rdr["COLUMN_NAME"];
                            cl.colLength = (string)rdr["CHARACTER_MAXIMUM_LENGTH"];
                            columnLists.Add(cl);
                        }
                    }
                    command.Dispose();
                    SQL = "INSERT INTO " + tableName + "(";
                    foreach (ColumnList cl in columnLists)
                    {
                        SQL += "[" + cl.colName + "]\r\n,";
                    }
                    SQL = SQL.TrimEnd(',');
                    SQL += ") SELECT * FROM OPENJSON('";
                    SQL += json;
                    SQL += "') WITH (";
                    foreach (ColumnList cl in columnLists)
                    {
                        SQL += "[" + cl.colName + "] varchar(" + cl.colLength + ") '$." + cl.colName + "'\r\n,";
                    }
                    SQL = SQL.TrimEnd(',');
                    SQL += ")";
                    //Console.WriteLine(SQL);
                    try
                    {
                        if (skip==0)
                        {
                            command = new SqlCommand("TRUNCATE TABLE " + tableName, conn);
                            command.ExecuteNonQuery();
                        }

                        command = new SqlCommand(SQL, conn);
                        command.ExecuteNonQuery();

                        conn.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        Log.Information(ex.Message);
                    }

                }
                   

                    skip += batchSize;
                    Console.WriteLine("Skip = " + skip);

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching data: {ex.Message}");
            Log.Information($"Error fetching data: {ex.Message} for {APIName} Criteria = {criteria}");
            await sendEmail(accessInfo.email_tenantId, accessInfo.email_clientId, accessInfo.email_secret, accessInfo.email_sender, accessInfo.email_recipient.Split(',').ToList(), $"Failure Fetching data from API {APIName}", ex.Message);


            moreRecords = false;
            }
        }

    return "done";
}





void buildConnection(SqlConnectionStringBuilder b, string ds, string ui, string pwd, string ic)
{
    b.DataSource = ds;
    b.UserID = ui;
    b.Password = pwd;
    b.InitialCatalog = ic;

}

void loadAccessInfo(SqlCommand cmd, AccessInfo accessInfo)
{
    using (SqlDataReader rdr = cmd.ExecuteReader())
    {
        while (rdr.Read())
        {
            accessInfo.Url = rdr["Url"].ToString();
            accessInfo.Grant_Type = rdr["Grant_Type"].ToString();
            accessInfo.Client_ID = rdr["Client_ID"].ToString();
            accessInfo.Scope = rdr["Scope"].ToString();
            accessInfo.Client_Secret = rdr["Client_Secret"].ToString();
            accessInfo.RootUrl = rdr["RootUrl"].ToString();
            accessInfo.ftpHost = rdr["ftpHost"].ToString();
            accessInfo.ftpUser = rdr["ftpUser"].ToString();
            accessInfo.ftpPass = rdr["ftpPass"].ToString();
            accessInfo.ftpRemoteFilePath = rdr["ftpRemoteFilePath"].ToString();
            accessInfo.ftpLocalFilePath = rdr["ftpLocalFilePath"].ToString();
            accessInfo.DataSource = rdr["DataSource"].ToString();
            accessInfo.InitialCatalog = rdr["InitialCatalog"].ToString();
            accessInfo.UserID = rdr["UserID"].ToString();
            accessInfo.Password = rdr["Password"].ToString();
            accessInfo.DayOffset = rdr["DayOffset"].ToString();
            accessInfo.email_tenantId = rdr["email_tenantId"].ToString();
            accessInfo.email_clientId = rdr["email_clientid"].ToString() ;
            accessInfo.email_secret = rdr["email_secret"].ToString();
            accessInfo.email_sender = rdr["email_sender"].ToString();
            accessInfo.email_recipient = rdr["email_recipient"].ToString();
                
        }
        rdr.Close();
    }

}
void addToContent(string contentLine)
    {
    string newLine = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} {contentLine}<br> ";
    content = content + newLine;
}

 static DateTime GetDateBasedOn1300()
{
    DateTime now = DateTime.Now;
    TimeSpan cutoffTime = new TimeSpan(13, 0, 0); // 1:00 PM

    return now.TimeOfDay < cutoffTime ? now.Date : now.Date.AddDays(1);
}



















