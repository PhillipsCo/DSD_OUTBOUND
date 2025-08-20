using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ftpTesting
{
    public static class logging
    {
       public static void LogIt(string msg)
        {
            String logFile = ConfigurationManager.AppSettings["logPath"];

            using StreamWriter SW = new StreamWriter(logFile,true);
            {
                SW.WriteLine($"{DateTime.Now} : {msg}");
            }

        }
        

    }
}
