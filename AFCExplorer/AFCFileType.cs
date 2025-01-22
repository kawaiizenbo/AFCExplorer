using System;

namespace AFCExplorer
{
    public class AFCFileType
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Modified { get; set; }
        public string Size { get; set; }

        public AFCFileType(string name, string type, string modified, string size)
        {
            Name = name;
            Type = type;
            Modified = modified;
            Size = size;
            if (Type == "Directory") Size = "";
        }
    }
}
