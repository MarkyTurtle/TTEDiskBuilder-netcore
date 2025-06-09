using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskCompiler.Commands
{

    public class DiskDefinition
    {
        public string? BootBlock {get;set;}
        public int DiskNumber {get;set;}
        public List<DiskItem>? DiskItems {get;set;}
    }

}