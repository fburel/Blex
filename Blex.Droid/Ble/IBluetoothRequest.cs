using Android.Bluetooth;

namespace Blex.Droid.Ble
{
    internal enum BluetoothRequestType
    {
//            CreateBond,
        Write,
        Read,

//            WriteDescriptor,
//            ReadDescriptor,
        EnableNotifications,
        EnableIndications,

//            DisableNotifications,
//            DisableIndications,
//            ReadBatteryLevel,
//            EnableBatteryLevelNotifications,
//            DisableBatteryLevelNotifications,
//            EnableServiceChangedIndications,
//            RequestMtu,
//            RequestConnectionPriority,
        Disconnect
    }

    internal interface IBluetoothRequest
    {
        BluetoothRequestType RequestType { get; }

        int RetryCount { get; }

        string Characteristic { get; }

        byte[] Value { get; }

        GattWriteType WriteType { get; }

        int IncrementRetryCount();

        void Resolve(byte[] result);

        void Reject(Error e);
    }
}