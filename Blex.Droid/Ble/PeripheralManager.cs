using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;

namespace Blex.Droid.Ble
{
    internal class PeripheralManager : RequestManager
    {
        private ConnectionRequest _connectionRequest;

        public PeripheralManager(Context context) : base(context)
        {
        }

        /*--------------------------------------------------------------------------------*/

        public event EventHandler<State> ConnectionStateChanged;

        public event EventHandler<BluetoothGattCharacteristic> CharacteristicUpdated;

        public Task Connect(BluetoothDevice device, double timeout)
        {
            var tr = new ConnectionRequest();
            
            _connectionRequest = tr;

            ConnectDevice(device);

            Task.Delay(TimeSpan.FromSeconds(timeout)).ContinueWith(t => {

                tr.Failed(Error.TimeOut);

            });
                
            return _connectionRequest.Task;
        }

        public Task Disconnect()
        {
            var request = new OperationRequest(BluetoothRequestType.Disconnect);

            Enqueue(request);

            return request.Task;
        }

        public Task Write(byte[] data, string characteristic, bool withoutResponse = false)
        {
            if (withoutResponse)
            {
                Write(GetCharacteristic(characteristic), data, GattWriteType.NoResponse);
                return null;
            }
            
            var request = new OperationRequest(BluetoothRequestType.Write, characteristic, data);

            Enqueue(request);

            return request.Task;
        }

        public Task<byte[]> Read(string characteristic)
        {
            var request = new OperationRequest(BluetoothRequestType.Read, characteristic);

            Enqueue(request);

            return request.Task;
        }

        public Task Subscribe(string characteristic)
        {
            var request =
                new OperationRequest(BluetoothRequestType.EnableNotifications, characteristic);

            Enqueue(request);

            return request.Task;
        }

        protected override void OnDeviceReady(BluetoothDevice device)
        {
            if (_connectionRequest == null) return;

            _connectionRequest?.Succeed();
            _connectionRequest = null;
        }

        protected override void OnStateChanged(State state)
        {
            if (state == State.Disconnected) ConnectionStateChanged?.Invoke(this, state);
        }

        protected override void OnCharacteristicNotified(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            CharacteristicUpdated?.Invoke(this, characteristic);
        }

        protected override void OnCharacteristicIndicated(BluetoothGatt gatt,
            BluetoothGattCharacteristic characteristic)
        {
            CharacteristicUpdated?.Invoke(this, characteristic);
        }

        protected override void OnError(BluetoothDevice device, Error errorType, GattStatus status)
        {
            if ((_connectionRequest?.IncreaseRetryCount() ?? int.MaxValue) < 3)
            {
                Task.Delay(1000).ContinueWith(t => { ConnectDevice(device); });
                return;
            }
            
            _connectionRequest?.Failed(errorType);

            base.OnError(device, errorType, status);
        }


        private class OperationRequest : IBluetoothRequest
        {
            private readonly TaskCompletionSource<byte[]> _operation;

            public OperationRequest(BluetoothRequestType type, string characteristic = null, byte[] value = null,
                GattWriteType writeType = GattWriteType.Default)
            {
                RequestType = type;
                Characteristic = characteristic;
                Value = value;
                WriteType = writeType;
                _operation = new TaskCompletionSource<byte[]>();
            }

            public Task<byte[]> Task => _operation.Task;
            public BluetoothRequestType RequestType { get; }
            public int RetryCount { get; private set; }
            public string Characteristic { get; }
            public byte[] Value { get; }
            public GattWriteType WriteType { get; }

            public int IncrementRetryCount()
            {
                return ++RetryCount;
            }

            public void Resolve(byte[] result)
            {
                _operation.TrySetResult(result);
            }

            public void Reject(Error e)
            {
                _operation.TrySetException(new BluetoothException(e));
            }
        }

        private class ConnectionRequest
        {
            private readonly TaskCompletionSource<TimeSpan> _completionSource;

            private readonly Stopwatch _stopwatch;

            public ConnectionRequest()
            {
                _stopwatch = new Stopwatch();
                _stopwatch.Start();
                _completionSource = new TaskCompletionSource<TimeSpan>();
            }

            private int _count;

            public Task<TimeSpan> Task => _completionSource.Task;

            public int IncreaseRetryCount()
            {
                return ++_count;
            }

            public void Succeed()
            {
                _stopwatch.Stop();
                _completionSource.TrySetResult(_stopwatch.Elapsed);
            }

            public void Failed(Error e)
            {
                _stopwatch.Stop();
                _completionSource.TrySetException(new BluetoothException(e));
            }
        }
    }
}