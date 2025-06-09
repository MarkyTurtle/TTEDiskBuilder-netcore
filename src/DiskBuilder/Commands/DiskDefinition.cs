using DiskBuilder.Commands;

namespace DiskCompiler.Commands
{

    public class DiskDefinition
    {
        public BootBlock BootBlock { get; set; }
        public int DiskNumber { get; set; }
        public List<DiskItem> DiskItems { get; set; }
        public byte[] DiskData { get; set; }

        public DiskDefinition()
        {
            BootBlock = new BootBlock();
            DiskNumber = 1;
            DiskItems = new List<DiskItem>();
            DiskData = new byte[0];
        }


        public void LoadDiskItems(string folderPath)
        {
            if (BootBlock != null)
            {
                if (!string.IsNullOrEmpty(BootBlock.FileName))
                {
                    var filePath = Path.Combine(folderPath, BootBlock.FileName);
                    BootBlock.FileData = File.ReadAllBytes(filePath);
                    BootBlock.FileSize = BootBlock.FileData.Length;

                    if (BootBlock.FileSize != 0x400)
                    {
                        throw new Exception("bootblock incorrect size");
                    }
                }
            }

            if (DiskItems != null)
            {
                foreach (var diskItem in DiskItems)
                {
                    LoadDiskItem(diskItem, folderPath);
                }
            }

        }


        protected void LoadDiskItem(DiskItem diskItem, string folderPath)
        {
            var sourceFile = Path.Combine(folderPath, diskItem.FileName);
            diskItem.FileData = File.ReadAllBytes(sourceFile);
            diskItem.FileSize = diskItem.FileData.Length;
        }


        public void MergeData()
        {
            var bytePosition = 0;
            using (var memoryStream = new MemoryStream())
            using (var writer = new BigEndianBinaryWriter(memoryStream))
            {
                foreach (var diskItem in DiskItems)
                {
                    diskItem.DiskLocation = bytePosition;
                    writer.Write(diskItem.FileData);
                    bytePosition += diskItem.FileSize;
                }
                DiskData = writer.ToArray();
            }

        }

    }

}