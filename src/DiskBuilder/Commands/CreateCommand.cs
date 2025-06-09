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
            DiskDefinition diskDefinition = GetDiskDefinitionFromJson(folderPath, configFilename);
            diskDefinition.LoadDiskItems(folderPath);
            diskDefinition.MergeData();

            var disk = MakeDisk(diskDefinition, folderPath);

            var diskOutFilepath = Path.Combine(folderPath, outFilename);
            File.WriteAllBytes(diskOutFilepath, disk);

            var fileTable = GetDiskContents(diskDefinition);
            var fileTableName = Path.Combine(folderPath, outFilename + ".filetable.txt");
            File.WriteAllText(fileTableName, fileTable);

            Console.WriteLine($"ADF Disk Image: {Path.GetFullPath(diskOutFilepath)}");
            Console.WriteLine($"ADF File Table: {Path.GetFullPath(fileTableName)}");

        }



        private static string GetDiskContents(DiskDefinition diskDefinition)
        {
            var fileTableSize = (diskDefinition.DiskItems.Count + 1) * 4 * 4;       // First entry = disk number, hence + 1
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

            // Write Disk Number Entry
            stringBuilder.Append("dsk#");
            stringBuilder.Append("\t");
            stringBuilder.Append(diskDefinition.DiskNumber.ToString("X8"));
            stringBuilder.Append("\t");
            stringBuilder.Append(diskDefinition.DiskNumber.ToString("X8"));
            stringBuilder.Append("\t");
            stringBuilder.Append(diskDefinition.DiskNumber.ToString("X8"));
            stringBuilder.Append(Environment.NewLine);

            // Create File Table in disk image
            foreach (var diskItem in diskDefinition.DiskItems)
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

            // Create Free Space Entry
            var lastDiskItem = diskDefinition.DiskItems.Last();
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



        private static byte[] MakeDisk(DiskDefinition diskDefinition, string folderPath)
        {
            var fileTableSize = (diskDefinition.DiskItems.Count + 1) * 4 * 4;       // First Entry = Disk Number, hence the + 1
            var offset = 0x400 + fileTableSize;

            using (var memoryStream = new MemoryStream())
            using (var writer = new BigEndianBinaryWriter(memoryStream))
            {

                // Create BootBlock if exists
                if (!string.IsNullOrEmpty(diskDefinition.BootBlock.FileName))
                {
                    InsertBootBlockCheckSum(diskDefinition.BootBlock.FileData);
                    writer.Write(diskDefinition.BootBlock.FileData);
                }
                else
                {
                    writer.Write(new byte[0x400]);
                }

                // Create Disk Number entry in FileTable
                writer.WriteAscii("dsk#", 4);
                writer.WriteInt32(diskDefinition.DiskNumber);
                writer.WriteInt32(diskDefinition.DiskNumber);
                writer.WriteInt32(diskDefinition.DiskNumber);

                // Create FileTable Entries for Files
                foreach (var diskItem in diskDefinition.DiskItems)
                {
                    writer.WriteAscii(diskItem.FileID, 4);               // file identifier (long)
                    writer.WriteInt32(diskItem.DiskLocation + offset);   // start offset from start of disk
                    writer.WriteInt32(0);                                // packed size
                    writer.WriteInt32(diskItem.FileSize);
                }

                // Write File Data
                writer.Write(diskDefinition.DiskData);

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




        private static DiskDefinition GetDiskDefinitionFromJson(string folderPath, string configFilename)
        {
            var configFilePath = Path.Combine(folderPath, configFilename);
            string jsonConfig = File.ReadAllText(configFilePath);

            var diskDefinition = JsonConvert.DeserializeObject<DiskDefinition>(jsonConfig);
            if (diskDefinition == null || diskDefinition.DiskItems == null || !diskDefinition.DiskItems.Any())
            {
                throw new Exception($"{configFilename} disk.json is empty or contains no disk items.");
            }

            return diskDefinition;
        }


    }


}
