using System;
using System.Collections.Generic;
using System.Text;
using Android.OS;
using Android.Util;
using Java.Nio;
using Java.Util;

namespace Blex.Droid.ScanHelper
{
    internal class ScanRecord
    {
        // The following data type values are assigned by Bluetooth SIG.
        // For more details refer to Bluetooth 4.1 specification, Volume 3, Part C, Section 18.
        private const int DATA_TYPE_FLAGS = 0x01;

        private const int DATA_TYPE_SERVICE_UUIDS_16_BIT_PARTIAL = 0x02;
        private const int DATA_TYPE_SERVICE_UUIDS_16_BIT_COMPLETE = 0x03;
        private const int DATA_TYPE_SERVICE_UUIDS_32_BIT_PARTIAL = 0x04;
        private const int DATA_TYPE_SERVICE_UUIDS_32_BIT_COMPLETE = 0x05;
        private const int DATA_TYPE_SERVICE_UUIDS_128_BIT_PARTIAL = 0x06;
        private const int DATA_TYPE_SERVICE_UUIDS_128_BIT_COMPLETE = 0x07;
        private const int DATA_TYPE_LOCAL_NAME_SHORT = 0x08;
        private const int DATA_TYPE_LOCAL_NAME_COMPLETE = 0x09;
        private const int DATA_TYPE_TX_POWER_LEVEL = 0x0A;
        private const int DATA_TYPE_SERVICE_DATA = 0x16;
        private const int DATA_TYPE_MANUFACTURER_SPECIFIC_DATA = 0xFF;

        private const string BASE_UUID = "00000000-0000-1000-8000-00805F9B34FB";

        /** Length of bytes for 16 bit UUID */
        private const int UUID_BYTES_16_BIT = 2;

        /** Length of bytes for 32 bit UUID */
        private const int UUID_BYTES_32_BIT = 4;

        /** Length of bytes for 128 bit UUID */
        private const int UUID_BYTES_128_BIT = 16;

        private ScanRecord(List<ParcelUuid> serviceUuids, SparseArray<byte[]> manufacturerData,
            Dictionary<ParcelUuid, byte[]> serviceData, int advertiseFlag, int txPowerLevel, string localName,
            byte[] scanRecord)
        {
            ServiceUuids = serviceUuids;
            ManufacturerSpecificData = manufacturerData;
            ServiceData = serviceData;
            DeviceName = localName;
            AdvertiseFlags = advertiseFlag;
            TxPowerLevel = txPowerLevel;
            Bytes = scanRecord;
        }

        public string DeviceName { get; set; }

        public IList<ParcelUuid> ServiceUuids { get; set; }

        public object Bytes { get; set; }

        public int TxPowerLevel { get; set; }

        public int AdvertiseFlags { get; set; }

        public Dictionary<ParcelUuid, byte[]> ServiceData { get; set; }

        public SparseArray<byte[]> ManufacturerSpecificData { get; set; }

        public static ScanRecord Parse(byte[] scanRecord)
        {
            if (scanRecord == null) return null;

            var currentPos = 0;
            var advertiseFlag = -1;
            var serviceUuids = new List<ParcelUuid>();
            string localName = null;
            var txPowerLevel = int.MinValue;

            var manufacturerData = new SparseArray<byte[]>();
            var serviceData = new Dictionary<ParcelUuid, byte[]>();

            while (currentPos < scanRecord.Length)
            {
                // length is unsigned int.
                var length = scanRecord[currentPos++] & 0xFF;
                if (length == 0) break;
                // Note the length includes the length of the field type itself.
                var dataLength = length - 1;
                // fieldType is unsigned int.
                var fieldType = scanRecord[currentPos++] & 0xFF;
                switch (fieldType)
                {
                    case DATA_TYPE_FLAGS:
                        advertiseFlag = scanRecord[currentPos] & 0xFF;
                        break;
                    case DATA_TYPE_SERVICE_UUIDS_16_BIT_PARTIAL:
                    case DATA_TYPE_SERVICE_UUIDS_16_BIT_COMPLETE:
                        ParseServiceUuid(scanRecord, currentPos,
                            dataLength, UUID_BYTES_16_BIT, serviceUuids);
                        break;
                    case DATA_TYPE_SERVICE_UUIDS_32_BIT_PARTIAL:
                    case DATA_TYPE_SERVICE_UUIDS_32_BIT_COMPLETE:
                        ParseServiceUuid(scanRecord, currentPos, dataLength,
                            UUID_BYTES_32_BIT, serviceUuids);
                        break;
                    case DATA_TYPE_SERVICE_UUIDS_128_BIT_PARTIAL:
                    case DATA_TYPE_SERVICE_UUIDS_128_BIT_COMPLETE:
                        ParseServiceUuid(scanRecord, currentPos, dataLength,
                            UUID_BYTES_128_BIT, serviceUuids);
                        break;
                    case DATA_TYPE_LOCAL_NAME_SHORT:
                    case DATA_TYPE_LOCAL_NAME_COMPLETE:
                        localName = Encoding.Default.GetString(ExtractBytes(scanRecord, currentPos, dataLength));
                        break;
                    case DATA_TYPE_TX_POWER_LEVEL:
                        txPowerLevel = scanRecord[currentPos];
                        break;
                    case DATA_TYPE_SERVICE_DATA:
                        // The first two bytes of the service data are service data UUID in little
                        // endian. The rest bytes are service data.
                        var serviceUuidLength = UUID_BYTES_16_BIT;
                        var serviceDataUuidBytes = ExtractBytes(scanRecord, currentPos,
                            serviceUuidLength);
                        var serviceDataUuid = ParseUuidFrom(serviceDataUuidBytes);
                        var serviceDataArray = ExtractBytes(scanRecord,
                            currentPos + serviceUuidLength, dataLength - serviceUuidLength);
                        serviceData[serviceDataUuid] = serviceDataArray;
                        break;
                    case DATA_TYPE_MANUFACTURER_SPECIFIC_DATA:
                        // The first two bytes of the manufacturer specific data are
                        // manufacturer ids in little endian.
                        var manufacturerId = ((scanRecord[currentPos + 1] & 0xFF) << 8) +
                                             (scanRecord[currentPos] & 0xFF);
                        var manufacturerDataBytes = ExtractBytes(scanRecord, currentPos + 2,
                            dataLength - 2);
                        manufacturerData.Put(manufacturerId, manufacturerDataBytes);
                        break;
                }

                currentPos += dataLength;
            }

            if (serviceUuids.Count == 0) serviceUuids = null;
            return new ScanRecord(serviceUuids, manufacturerData, serviceData,
                advertiseFlag, txPowerLevel, localName, scanRecord);
        }

        private static int ParseServiceUuid(byte[] scanRecord, int currentPos, int dataLength,
            int uuidLength, List<ParcelUuid> serviceUuids)
        {
            while (dataLength > 0)
            {
                var uuidBytes = ExtractBytes(scanRecord, currentPos,
                    uuidLength);
                serviceUuids.Add(ParseUuidFrom(uuidBytes));
                dataLength -= uuidLength;
                currentPos += uuidLength;
            }

            return currentPos;
        }

        // Helper method to extract bytes from byte array.
        private static byte[] ExtractBytes(byte[] scanRecord, int start, int length)
        {
            var bytes = new byte[length];
            Array.Copy(scanRecord, start, bytes, 0, length);
            return bytes;
        }

        private static ParcelUuid ParseUuidFrom(byte[] uuidBytes)
        {
            if (uuidBytes == null) throw new Exception("uuidBytes cannot be null");
            var length = uuidBytes.Length;
            if (length != UUID_BYTES_16_BIT && length != UUID_BYTES_32_BIT &&
                length != UUID_BYTES_128_BIT)
                throw new Exception("uuidBytes length invalid - " + length);

            long msb, lsb;

            // Construct a 128 bit UUID.
            if (length == UUID_BYTES_128_BIT)
            {
                var buf = ByteBuffer.Wrap(uuidBytes).Order(ByteOrder.LittleEndian);
                msb = buf.GetLong(8);
                lsb = buf.GetLong(0);
                return new ParcelUuid(new UUID(msb, lsb));
            }

            // For 16 bit and 32 bit UUID we need to convert them to 128 bit value.
            // 128_bit_value = uuid * 2^96 + BASE_UUID
            long shortUuid;
            if (length == UUID_BYTES_16_BIT)
            {
                shortUuid = uuidBytes[0] & 0xFF;
                shortUuid += (uuidBytes[1] & 0xFF) << 8;
            }
            else
            {
                shortUuid = uuidBytes[0] & 0xFF;
                shortUuid += (uuidBytes[1] & 0xFF) << 8;
                shortUuid += (uuidBytes[2] & 0xFF) << 16;
                shortUuid += (uuidBytes[3] & 0xFF) << 24;
            }

            msb = ParcelUuid.FromString(BASE_UUID).Uuid.MostSignificantBits + (shortUuid << 32);
            lsb = ParcelUuid.FromString(BASE_UUID).Uuid.LeastSignificantBits;
            return new ParcelUuid(new UUID(msb, lsb));
        }
    }
}