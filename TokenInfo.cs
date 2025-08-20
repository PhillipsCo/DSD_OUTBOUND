using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCoutbound
{
    internal class TokenInfo
    {
        public String? token_type { get; set; }
        public String? expires_in { get; set; }
        public String? ext_expires_in { get; set; }
        public String? access_token { get; set; }
    }
}
