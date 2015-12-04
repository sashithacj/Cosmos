﻿using Cosmos.Common.Extensions;
using Cosmos.System.FileSystem.Listing;

using global::System;
using global::System.Collections.Generic;

namespace Cosmos.System.FileSystem.FAT.Listing
{
    using global::System.IO;

    internal class FatDirectoryEntry : DirectoryEntry
    {
        private readonly uint mEntryHeaderDataOffset;

        private new readonly FatFileSystem mFileSystem;

        private readonly ulong mFirstClusterNum;

        private new readonly FatDirectoryEntry mParent;

        // Size is UInt32 because FAT doesn't support bigger.
        // Don't change to UInt64
        public FatDirectoryEntry(
            FatFileSystem aFileSystem,
            FatDirectoryEntry aParent,
            string aName,
            uint aSize,
            ulong aFirstCluster,
            uint aEntryHeaderDataOffset,
            DirectoryEntryTypeEnum aEntryType)
            : base(aFileSystem, aParent, aName, aSize, aEntryType)
        {
            if (aFileSystem == null)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aFileSystem is null.");
                throw new ArgumentNullException(nameof(aFileSystem));
            }

            if (aName == null)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aName is null.");
                throw new ArgumentNullException(nameof(aName));
            }

            if (aFirstCluster < 2)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aFirstCluster is out of range.");
                throw new ArgumentOutOfRangeException(nameof(aFirstCluster));
            }

            FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aParent.Name =", aParent?.mName, ", aName =", aName, ", aSize = " + aSize, ", aFirstCluster =", aFirstCluster, ", aEntryHeaderDataOffset =", aEntryHeaderDataOffset);

            mFileSystem = aFileSystem;
            mParent = aParent;
            mFirstClusterNum = aFirstCluster;
            mEntryHeaderDataOffset = aEntryHeaderDataOffset;
        }

        public FatDirectoryEntry(
            FatFileSystem aFileSystem,
            FatDirectoryEntry aParent,
            string aName,
            ulong aFirstCluster)
            : base(aFileSystem, aParent, aName, 0, DirectoryEntryTypeEnum.Directory)
        {
            if (aFileSystem == null)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aFileSystem is null.");
                throw new ArgumentNullException(nameof(aFileSystem));
            }

            if (aName == null)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aName is null.");
                throw new ArgumentNullException(nameof(aName));
            }

            if (aFirstCluster < 2)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aFirstCluster is out of range.");
                throw new ArgumentOutOfRangeException(nameof(aFirstCluster));
            }

            FileSystemHelpers.Debug("FatDirectoryEntry.ctor", "aParent.Name =", aParent?.mName, ", aName =", aName, ", aFirstCluster =", aFirstCluster);

            mFileSystem = aFileSystem;
            mParent = aParent;
            mFirstClusterNum = aFirstCluster;
            mEntryHeaderDataOffset = 0;
        }

        public ulong[] GetFatTable()
        {
            var xFat = mFileSystem.GetFat(0);
            if (xFat != null)
            {
                return xFat.GetFatChain(mFirstClusterNum, mSize);
            }
            return null;
        }

        public FatFileSystem GetFileSystem()
        {
            return mFileSystem;
        }

        public override Stream GetFileStream()
        {
            if (mEntryType == DirectoryEntryTypeEnum.File)
            {
                return new FatStream(this);
            }

            return null;
        }

        public override void SetName(string aName)
        {
            if (mParent == null)
            {
                throw new Exception("Parent entry is null. The name cannot be set.");
            }

            FileSystemHelpers.Debug("FatDirectoryEntry.SetName", "mName =", mName, ", mSize =", mSize, ", aName =", aName);
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.ShortName, aName);
        }

        public override void SetSize(long aSize)
        {
            if (mParent == null)
            {
                throw new Exception("Parent entry is null. The size cannot be set.");
            }

            FileSystemHelpers.Debug("FatDirectoryEntry.SetSize", "mName =", mName, ", mSize =", mSize, ", aSize =", aSize);
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.Size, (uint)aSize);
        }

        private void AllocateDirectoryEntry()
        {
            // TODO: Deal with short and long name.
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.ShortName, mName);
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.Attributes, FatDirectoryEntryAttributeConsts.Directory);
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.FirstClusterHigh, (uint)(mFirstClusterNum >> 16));
            SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata.FirstClusterLow, (uint)(mFirstClusterNum & 0xFFFF));
            byte[] xData = GetDirectoryEntryData();
            SetDirectoryEntryData(xData);
        }

        public FatDirectoryEntry AddDirectoryEntry(string aName, DirectoryEntryTypeEnum aType)
        {
            FileSystemHelpers.Debug("FatDirectoryEntry.AddDirectoryEntry", "aName =", aName, ", aType =", aType.ToString());
            if (aType == DirectoryEntryTypeEnum.Directory)
            {
                uint xFirstCluster = mFileSystem.GetFat(0).GetNextUnallocatedFatEntry();
                uint xEntryHeaderDataOffset = GetNextUnallocatedEntry();
                var xNewEntry = new FatDirectoryEntry(
                    mFileSystem,
                    this,
                    aName,
                    0,
                    xFirstCluster,
                    xEntryHeaderDataOffset,
                    aType);
                xNewEntry.AllocateDirectoryEntry();
                return xNewEntry;
            }
            if (aType == DirectoryEntryTypeEnum.File)
            {
                throw new NotImplementedException("Creating new files is currently not implemented.");
            }
            throw new ArgumentOutOfRangeException("aType", "Unknown directory entry type.");
        }

        public List<FatDirectoryEntry> ReadDirectoryContents()
        {
            FileSystemHelpers.Debug("FatDirectoryEntry.ReadDirectoryContents");
            var xData = GetDirectoryEntryData();
            var xResult = new List<FatDirectoryEntry>();
            FatDirectoryEntry xParent = (FatDirectoryEntry)(mParent ?? mFileSystem.GetRootDirectory());

            //TODO: Change xLongName to StringBuilder
            string xLongName = "";
            string xName = "";
            for (uint i = 0; i < xData.Length; i = i + 32)
            {
                FileSystemHelpers.Debug("-------------------------------------------------");
                byte xAttrib = xData[i + 11];
                byte xStatus = xData[i];

                FileSystemHelpers.Debug("Attrib =", xAttrib, ", Status =", xStatus);
                if (xAttrib == FatDirectoryEntryAttributeConsts.LongName)
                {
                    byte xType = xData[i + 12];
                    byte xOrd = xData[i];
                    FileSystemHelpers.Debug("Reading LFN with Seqnr " + xOrd, ", Type =", xType);
                    if (xOrd == 0xE5)
                    {
                        FileSystemHelpers.Debug("<DELETED>", "Attrib =", xAttrib, ", Status =", xStatus);
                        continue;
                    }
                    if (xType == 0)
                    {
                        if ((xOrd & 0x40) > 0)
                        {
                            xLongName = "";
                        }
                        //TODO: Check LDIR_Ord for ordering and throw exception
                        // if entries are found out of order.
                        // Also save buffer and only copy name if a end Ord marker is found.
                        string xLongPart = xData.GetUtf16String(i + 1, 5);
                        // We have to check the length because 0xFFFF is a valid Unicode codepoint.
                        // So we only want to stop if the 0xFFFF is AFTER a 0x0000. We can determin
                        // this by also looking at the length. Since we short circuit the or, the length
                        // is rarely evaluated.
                        if (xData.ToUInt16(i + 14) != 0xFFFF || xLongPart.Length == 5)
                        {
                            xLongPart = xLongPart + xData.GetUtf16String(i + 14, 6);
                            if (xData.ToUInt16(i + 28) != 0xFFFF || xLongPart.Length == 11)
                            {
                                xLongPart = xLongPart + xData.GetUtf16String(i + 28, 2);
                            }
                        }
                        xLongName = xLongPart + xLongName;
                        xLongPart = null;
                        //TODO: LDIR_Chksum
                    }
                }
                else
                {
                    xName = xLongName;
                    if (xStatus == 0x00)
                    {
                        FileSystemHelpers.Debug("<EOF>", "Attrib =", xAttrib, ", Status =", xStatus);
                        break;
                    }
                    switch (xStatus)
                    {
                        case 0x05:
                            // Japanese characters - We dont handle these
                            break;
                        case 0xE5:
                            // Empty slot, skip it
                            break;
                        default:
                            if (xStatus >= 0x20)
                            {
                                if (xLongName.Length > 0)
                                {
                                    // Leading and trailing spaces are to be ignored according to spec.
                                    // Many programs (including Windows) pad trailing spaces although it
                                    // it is not required for long names.
                                    // As per spec, ignore trailing periods
                                    xName = xLongName.Trim();

                                    //If there are trailing periods
                                    int nameIndex = xName.Length - 1;
                                    if (xName[nameIndex] == '.')
                                    {
                                        //Search backwards till we find the first non-period character
                                        for (; nameIndex > 0; nameIndex--)
                                        {
                                            if (xName[nameIndex] != '.')
                                            {
                                                break;
                                            }
                                        }
                                        //Substring to remove the periods
                                        xName = xName.Substring(0, nameIndex + 1);
                                    }
                                    xLongName = "";
                                }
                                else
                                {
                                    string xEntry = xData.GetAsciiString(i, 11);
                                    xName = xEntry.Substring(0, 8).TrimEnd();
                                    string xExt = xEntry.Substring(8, 3).TrimEnd();
                                    if (xExt.Length > 0)
                                    {
                                        xName = xName + "." + xExt;
                                    }
                                }
                            }
                            break;
                    }
                }
                uint xFirstCluster = (uint)(xData.ToUInt16(i + 20) << 16 | xData.ToUInt16(i + 26));

                int xTest = xAttrib & (FatDirectoryEntryAttributeConsts.Directory | FatDirectoryEntryAttributeConsts.VolumeID);
                if (xAttrib == FatDirectoryEntryAttributeConsts.LongName)
                {
                    // skip adding, as it's a LongFileName entry, meaning the next normal entry is the item with the name.
                    FileSystemHelpers.Debug("Entry was a Long FileName entry. Current LongName = '" + xLongName + "'");
                }
                else if (xTest == 0)
                {
                    uint xSize = xData.ToUInt32(i + 28);
                    if (xSize == 0 && xName.Length == 0)
                    {
                        continue;
                    }
                    var xEntry = new FatDirectoryEntry(mFileSystem, xParent, xName, xSize, xFirstCluster, i, DirectoryEntryTypeEnum.File);
                    xResult.Add(xEntry);
                    FileSystemHelpers.Debug(xEntry.mName + " -" + xEntry.mSize + " bytes");
                }
                else if (xTest == FatDirectoryEntryAttributeConsts.Directory)
                {
                    uint xSize = xData.ToUInt32(i + 28);
                    var xEntry = new FatDirectoryEntry(mFileSystem, xParent, xName, xSize, xFirstCluster, i, DirectoryEntryTypeEnum.Directory);
                    FileSystemHelpers.Debug(xEntry.mName + " <DIR> " + xEntry.mSize + " bytes", "Attrib =", xAttrib, ", Status =", xStatus);
                    xResult.Add(xEntry);
                }
                else if (xTest == FatDirectoryEntryAttributeConsts.VolumeID)
                {
                    FileSystemHelpers.Debug("<VOLUME ID>", "Attrib =", xAttrib, ", Status =", xStatus);
                }
                else
                {
                    FileSystemHelpers.Debug("<INVALID ENTRY>", "Attrib =", xAttrib, ", Status =", xStatus);
                }
            }

            return xResult;
        }

        private uint GetNextUnallocatedEntry()
        {
            var xData = GetDirectoryEntryData();
            FileSystemHelpers.Debug("FatDirectoryEntry.GetNextUnallocatedEntry", " xData.Length =", xData.Length);
            for (uint i = 0; i < xData.Length; i += 32)
            {
                uint x1 = xData.ToUInt32(i);
                uint x2 = xData.ToUInt32(i + 8);
                uint x3 = xData.ToUInt32(i + 16);
                uint x4 = xData.ToUInt32(i + 24);
                if ((x1 == 0) && (x2 == 0) && (x3 == 0) && (x4 == 0))
                {
                    FileSystemHelpers.Debug("Found unallocated Directory entry", i);
                    return i;
                }
            }

            // TODO: What should we return if no available entry is found.
            throw new Exception("Failed to find an unallocated directory entry.");
        }

        private byte[] GetDirectoryEntryData()
        {
            FileSystemHelpers.Debug("FatDirectoryEntry.GetDirectoryEntryData");
            if (mEntryType != DirectoryEntryTypeEnum.Unknown)
            {
                byte[] xData;
                mFileSystem.Read(mFirstClusterNum, out xData);
                return xData;
            }

            throw new Exception("Invalid directory entry type");
        }

        private void SetDirectoryEntryData(byte[] aData)
        {
            if (aData == null)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.SetDirectoryEntryData", "aData is null.");
                throw new ArgumentNullException("aData");
            }

            if (aData.Length == 0)
            {
                FileSystemHelpers.Debug("FatDirectoryEntry.SetDirectoryEntryData", "aData length is 0.");
                return;
            }

            FileSystemHelpers.Debug("SetDirectoryEntryData: Name =", mName);
            FileSystemHelpers.Debug("SetDirectoryEntryData: Size =", mSize);
            FileSystemHelpers.Debug("SetDirectoryEntryData: FirstClusterNum =", mFirstClusterNum);
            FileSystemHelpers.Debug("SetDirectoryEntryData: aData.Length =", aData.Length);

            if (mEntryType != DirectoryEntryTypeEnum.Unknown)
            {
                mFileSystem.Write(mFirstClusterNum, aData);
            }
            else
            {
                throw new Exception("Invalid directory entry type");
            }
        }

        internal void SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata aEntryMetadata, uint aValue)
        {
            if (mParent != null)
            {
                var xData = mParent.GetDirectoryEntryData();
                if (xData.Length > 0)
                {
                    var xValue = new byte[aEntryMetadata.DataLength];
                    xValue.SetUInt32(0, aValue);

                    uint offset = mEntryHeaderDataOffset + aEntryMetadata.DataOffset;

                    Array.Copy(xValue, 0, xData, offset, aEntryMetadata.DataLength);

                    FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: DataLength =", aEntryMetadata.DataLength);
                    FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: DataOffset =", aEntryMetadata.DataOffset);
                    FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: EntryHeaderDataOffset =", mEntryHeaderDataOffset);
                    FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: TotalOffset =", offset);
                    FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: aValue =", aValue);

                    mParent.SetDirectoryEntryData(xData);
                }
            }
            else
            {
                throw new Exception("Root directory metadata can not be changed using the file stream.");
            }
        }

        internal void SetDirectoryEntryMetadataValue(FatDirectoryEntryMetadata aEntryMetadata, string aValue)
        {
            var xData = mParent.GetDirectoryEntryData();
            if (xData.Length > 0)
            {
                var xValue = new byte[aEntryMetadata.DataLength];
                xValue = aValue.GetUtf8Bytes(0, aEntryMetadata.DataLength);

                uint offset = mEntryHeaderDataOffset + aEntryMetadata.DataOffset;

                Array.Copy(xValue, 0, xData, offset, aEntryMetadata.DataLength);

                FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: DataLength =", aEntryMetadata.DataLength);
                FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: DataOffset =", aEntryMetadata.DataOffset);
                FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: EntryHeaderDataOffset =", mEntryHeaderDataOffset);
                FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: TotalOffset =", offset);
                FileSystemHelpers.Debug("SetDirectoryEntryMetadataValue: aValue =", aValue);

                mParent.SetDirectoryEntryData(xData);
            }
        }
    }
}