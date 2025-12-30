using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;

namespace DSD_Outbound.Services
{
    internal class EmailService
    {
        async Task sendEmail(string tenantId, string clientId, string clientSecret, string senderEmail, List<string> recipientEmails, string subject, string content)
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

    }
}
