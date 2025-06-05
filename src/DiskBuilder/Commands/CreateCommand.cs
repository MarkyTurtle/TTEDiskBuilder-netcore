using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DiskCompiler.Commands
{
    internal class CreateCommand
    {

        public static void AddCreateCommand(Command parentCommand)
        {
            var cmdOutOption = new Option<string>(
                name: "--out",
                description: "output <filename>.adf filename")
            {
                IsRequired = true
            };

            var cmdPathOption = new Option<string?>(
                name: "--path",
                description: "folder path containing disk.json and disk files.")
            {
                IsRequired = false
            };

            var cmdConfigOption = new Option<string?>(
                name: "--config",
                description: "config json file to use for disk manifest.")
            {
                IsRequired = false
            };

            var createCommand = new Command("create", "Create a new .ADF disk image.")
            {
                cmdOutOption,
                cmdPathOption,
                cmdConfigOption
            };

            parentCommand.AddCommand(createCommand);


            createCommand.SetHandler((outFilename, folderPath, configFileName) =>
            {
                if (string.IsNullOrEmpty(outFilename))
                {
                    outFilename = "disk.adf";
                }

                if (!outFilename.EndsWith(".adf", StringComparison.OrdinalIgnoreCase))
                {
                    outFilename += ".adf";
                }

                if (string.IsNullOrEmpty(folderPath))
                {
                    folderPath = Environment.CurrentDirectory;
                }
                else
                {
                    folderPath = folderPath.Replace('\\', Path.DirectorySeparatorChar);
                }

                if (string.IsNullOrEmpty(configFileName))
                {
                    configFileName = Path.GetFileName("disk.json");
                }
                CreateHandler(outFilename, folderPath, configFileName);
            },
            cmdOutOption,
            cmdPathOption,
            cmdConfigOption);

        }




        private static void CreateHandler(string outFilename, string folderPath, string configFilename)
        {
            var diskBytes = new byte[0xdc000];

            var diskItems = GetDiskItemsFromJson(folderPath, configFilename);
            LoadDiskItems(diskItems, folderPath);
            byte[] diskData = MergeData(diskItems);
            var disk = MakeDisk(diskItems, diskData, folderPath);

            var outFilepath = Path.Combine(folderPath, outFilename);
            File.WriteAllBytes(outFilepath, disk);

            var fileTable = GetDiskContents(diskItems);
            File.WriteAllText(outFilepath + ".filetable.txt", fileTable);

            Console.WriteLine(outFilename);

        }



        private static string GetDiskContents(List<DiskItem> diskItems)
        {
            var fileTableSize = diskItems.Count * 4 * 4;
            var offset = 0x400 + fileTableSize;

            var stringBuilder = new StringBuilder();
            // write headers
            stringBuilder.Append("FileID");             // file identifier (long)
            stringBuilder.Append("\t");
            stringBuilder.Append("Disk Offset");        // start offset from start of disk
            stringBuilder.Append("\t");
            stringBuilder.Append("PackedSize");         // packed size
            stringBuilder.Append("\t");
            stringBuilder.Append("FileSize");           // file size
            stringBuilder.Append(Environment.NewLine);

            // Create File Table in disk image
            foreach (var diskItem in diskItems)
            {
                stringBuilder.Append(diskItem.FileID);                  // file identifier (long)
                stringBuilder.Append("\t");
                stringBuilder.Append((diskItem.DiskLocation + offset).ToString("X8"));   // start offset from start of disk
                stringBuilder.Append("\t");
                stringBuilder.Append(0.ToString("X8"));                                // packed size
                stringBuilder.Append("\t");
                stringBuilder.Append(diskItem.FileSize.ToString("X8"));
                stringBuilder.Append(Environment.NewLine);
            }

            var lastDiskItem = diskItems.Last();
            var freespaceStart = lastDiskItem.DiskLocation + lastDiskItem.FileSize + offset;
            var freespaceSize = 0xdc000 - freespaceStart; 

            // FreeSpace
            stringBuilder.Append("Free\t");
            stringBuilder.Append(freespaceStart.ToString("X8"));
            stringBuilder.Append("\t");
            stringBuilder.Append(0.ToString("X8"));                                // packed size
            stringBuilder.Append("\t");
            stringBuilder.Append(freespaceSize.ToString("X8"));
            stringBuilder.Append(Environment.NewLine);

            return stringBuilder.ToString();
        }



        private static byte[] MakeDisk(List<DiskItem> diskItems, byte[] diskData, string folderPath)
        {
            var fileTableSize = diskItems.Count * 4 * 4;
            var offset = 0x400 + fileTableSize;

            using (var memoryStream = new MemoryStream())
            using (var writer = new BigEndianBinaryWriter(memoryStream))
            {

                // Create BootBlock
                var bootFilePath = Path.Combine(folderPath, "bootblock.bin");
                if (!File.Exists(bootFilePath))
                {
                    throw new Exception("missing bootblock file");
                }
                byte[] bootBlock = File.ReadAllBytes(bootFilePath);
                if (bootBlock.Length != 0x400)
                {
                    throw new Exception("bootblock incorrect size");
                }

                InsertBootBlockCheckSum(bootBlock);
                writer.Write(bootBlock);


                // Create File Table in disk image
                foreach (var diskItem in diskItems)
                {
                    writer.WriteAscii(diskItem.FileID, 4);               // file identifier (long)
                    writer.WriteInt32(diskItem.DiskLocation + offset);   // start offset from start of disk
                    writer.WriteInt32(0);                                // packed size
                    writer.WriteInt32(diskItem.FileSize);
                }

                // Write File Data
                writer.Write(diskData);

                // Calc empty space
                var spaceNeeded = 0xdc000 - writer.Position;
                if (spaceNeeded < 0)
                {
                    throw new Exception($"disk is {-spaceNeeded} bytes over budget!");
                }

                var spacer = new byte[spaceNeeded];
                writer.Write(spacer);

                Console.WriteLine($"FYI: you have {spaceNeeded} bytes remaining on this disk");

                return writer.ToArray();
            }

        }



        /// <summary>
        /// Check sum algorithm.
        /// Create a Sum of all long words of the boot block (assuming the checksum value is 0 to start at byte offsets 4-7)
        /// If there is an overflow in the addition then add 1 to the checksum total.
        /// 
        /// A standard disk boot block is 1024 bytes or 256 longwords
        /// 
        /// </summary>
        /// <param name="bootBlock"></param>
        private static void InsertBootBlockCheckSum(byte[] bootBlock)
        {
            uint checksum = 0;
            SetBootBlockChecksum(bootBlock, checksum);
            checksum = CalculateBootBlockCheckSum(bootBlock);
            SetBootBlockChecksum(bootBlock, checksum);

        }



        private static void SetBootBlockChecksum(byte[] bootBlock, uint checksumValue)
        {
            var bytes = BitConverter.GetBytes(checksumValue);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            bootBlock[4] = bytes[0];
            bootBlock[5] = bytes[1];
            bootBlock[6] = bytes[2];
            bootBlock[7] = bytes[3];
        }



        private static uint CalculateBootBlockCheckSum(byte[] bootBlock)
        {
            uint checksum = 0;
            for (int i = 0; i < 0x400; i += 4)
            {
                uint precsum = checksum;
                uint currentLong = (uint)((bootBlock[i] << 24) | (bootBlock[i + 1] << 16) | (bootBlock[i + 2] << 8) | bootBlock[i + 3]);

                checksum += currentLong;
                if (checksum < precsum)
                {
                    checksum++;
                }
            }
            checksum = ~checksum;

            return checksum;

        }



        private static byte[] MergeData(List<DiskItem> diskItems)
        {
            var bytePosition = 0;
            using (var memoryStream = new MemoryStream())
            using (var writer = new BigEndianBinaryWriter(memoryStream))
            {
                foreach (var diskItem in diskItems)
                {
                    diskItem.DiskLocation = bytePosition;
                    writer.Write(diskItem.FileData);
                    bytePosition += diskItem.FileSize;
                }
                return writer.ToArray();
            }

        }



        private static void LoadDiskItems(List<DiskItem> diskItems, string folderPath)
        {
            foreach (var diskItem in diskItems)
            {
                LoadDiskItem(diskItem, folderPath);
            }

        }



        private static void LoadDiskItem(DiskItem diskItem, string folderPath)
        {
            var sourceFile = Path.Combine(folderPath, diskItem.FileName);
            diskItem.FileData = File.ReadAllBytes(sourceFile);
            diskItem.FileSize = diskItem.FileData.Length;
        }



        private static List<DiskItem> GetDiskItemsFromJson(string folderPath, string configFilename)
        {
            var configFilePath = Path.Combine(folderPath, configFilename);
            string jsonConfig = File.ReadAllText(configFilePath);

            var diskItems = JsonConvert.DeserializeObject<List<DiskItem>>(jsonConfig);
            if (diskItems == null || !diskItems.Any())
            {
                throw new Exception($"{configFilename} contains no disk items.");
            }

            return diskItems;
        }


    }


}
