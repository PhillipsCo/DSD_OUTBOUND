using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCoutbound
{
    internal class AccessInfo
    {
        public String? Url { get; set; }
        public String? Grant_Type { get; set; }
        //public String? Token_URL { get; set; }

        public String? Client_ID { get; set; }

        public String? Client_Secret { get; set; }

        public String? Scope { get; set; }
        public String? RootUrl { get; set; }

        public String? path { get; set; }
        public String? ftpHost { get; set; }
        public String? ftpUser { get; set; }
        public String? ftpPass { get; set; }

        public String? ftpRemoteFilePath { get; set; }
        public String? ftpLocalFilePath { get; set; }

        public String? DataSource { get; set; }
        public String? InitialCatalog { get; set; }

        public String? UserID { get; set; }
        public String? Password { get; set; }

        public String? DayOffset { get; set; }
        public String? email_tenantId { get; set; }
        public String? email_clientId { get; set; }
        public String? email_secret { get; set; }
        public String? email_sender { get; set; }
        public String? email_recipient { get; set; }
    }
}
