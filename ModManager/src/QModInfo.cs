using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ModManager
{
    public class QModInfo
    {
        public string Name;
        public string Author;
        public string CloudName;
        public string InlineDescription;
        public string Description;
        public string Site;
        public string[] RequiredModNames;
        public string CurrentVersion;
    }
}
