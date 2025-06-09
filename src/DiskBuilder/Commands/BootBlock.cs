namespace DiskBuilder.Commands
{
    public class BootBlock
    {
        public string FileName { get; set; }
        public byte[] FileData { get; set; }
        public int FileSize { get; set; }

        public BootBlock()
        {
            FileName = string.Empty;
            FileData = new byte[0];
        }
    }
}
