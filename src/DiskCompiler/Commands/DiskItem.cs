using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskCompiler.Commands
{
    public class DiskItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FileID { get; set; } = string.Empty;
        public bool Cacheable { get; set; }
        public byte[] FileData { get; set; } = [];
        public int FileSize { get; set; } 
        public int DiskLocation { get; set; }

    }

}
