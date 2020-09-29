using System.Collections.Generic;
using System.Linq;
using Timer = System.Timers.Timer;
using Android.Bluetooth;
using Android.Content;

namespace Blex.Droid.Ble
{
    internal abstract class RequestManager : BleManager
    {
        private const int DelayBetweenTwoCalls = 3000;
        private const int TimeOutDelay = 3000;

        private static readonly object pullLock = new object();

        private readonly LinkedList<IBluetoothRequest> _taskQueue = new LinkedList<IBluetoothRequest>();

        public readonly Dictionary<string, BluetoothGattCharacteristic> DiscoveredCharacteristics =
            new Dictionary<string, BluetoothGattCharacteristic>();

        /**
         * This flag is required to resume operations after the connection priority request was made.
         * It is used only on Android Oreo and newer, as only there there is onConnectionUpdated callback.
         * However, as this callback is triggered every time the connection parameters change, even
         * when such request wasn't made, this flag ensures the nextRequest() method won't be called
         * during another operation.
         */
        private bool _connectionPriorityOperationInProgress;
        private bool _operationInProgress = true; // Initially true to block operations before services are discovered.

        // timer used for timout and delay between 2 calls
        private Timer _timer;

        private IBluetoothRequest CurrentRequest;

        /**
         * Enqueues a new request. The request will be handled immediately if there is no operation in progress,
         * or automatically after the last enqueued one will finish.
         * <p>This method should be used to read and write data from the target device as it ensures that the last operation has finished
         * before a new one will be called.</p>
         *
         * @param request new request to be added to the queue.
         * @return true if request has been enqueued, false if the {@link #connect(BluetoothDevice)} method was not called before,
         * or the manager was closed using {@link #close()}.
         */
        protected void Enqueue(IBluetoothRequest request)
        {
            lock (pullLock)
            {
                _taskQueue.AddLast(request);
                NextRequest();
            }
        }

        private IBluetoothRequest Pull()
        {
            if (!_taskQueue?.Any() ?? true) return null;

            var request = _taskQueue.First();

            _taskQueue.RemoveFirst();

            return request;
        }

        private void NextRequest()
        {
            lock (pullLock)
            {
                if (_operationInProgress || (CurrentRequest = Pull()) == null) return;

                _operationInProgress = true;

                // timer to Timeout
                if (_timer != null) _timer.Enabled = false;
                _timer = new Timer(TimeOutDelay);
                // Tell the timer what to do when it elapses
                _timer.Elapsed += (s, e) => { OnError(null, Error.TimeOut, GattStatus.Failure); };
                _timer.Interval = TimeOutDelay;
                _timer.Start();

                Error result;
                switch (CurrentRequest.RequestType)
                {
                    case BluetoothRequestType.Write:

                        result = Write(GetCharacteristic(CurrentRequest.Characteristic), CurrentRequest.Value,
                            CurrentRequest.WriteType);
                        break;
                    case BluetoothRequestType.Read:
                        result = Read(GetCharacteristic(CurrentRequest.Characteristic));
                        break;
                    case BluetoothRequestType.EnableNotifications:
                        result = EnableNotifications(GetCharacteristic(CurrentRequest.Characteristic));
                        break;
                    case BluetoothRequestType.EnableIndications:
                        result = EnableIndications(GetCharacteristic(CurrentRequest.Characteristic));
                        break;
                    case BluetoothRequestType.Disconnect:
                        result = DisconnectDevice();
                        break;
                    default:
                        result = Error.Unknown;
                        break;
                }

                // The result may be false if given characteristic or descriptor were not found on the device,
                // or the feature is not supported on the Android.
                // In that case, proceed with next operation and ignore the one that failed.
                if (result != Error.Success)
                {
                    _connectionPriorityOperationInProgress = false;
                    _operationInProgress = false;
                    if (_timer != null) _timer.Enabled = false;
                    CurrentRequest?.Reject(result);
                    NextRequest();
                }
            }
        }

        protected BluetoothGattCharacteristic GetCharacteristic(string uuid)
        {
            return DiscoveredCharacteristics.GetValueOrDefault(uuid.ToString().ToLower(), null);
        }

        #region implements

        protected RequestManager(Context context) : base(context)
        {
        }

        protected override void OnServiceDiscovered(BluetoothGatt gatt)
        {
            DiscoveredCharacteristics.Clear();

            foreach (var service in gatt.Services)
            foreach (var characteristic in service.Characteristics)
            {
                var charUuid = characteristic.Uuid.ToString().ToLower();
                DiscoveredCharacteristics.Add(charUuid, characteristic);
//                Logger?.v("characteristic {charUuid} added");
            }

            ;


            _operationInProgress = false;

            _taskQueue.Clear();

            OnDeviceReady(gatt.Device);

//            Logger?.v("Device Ready - Start processing request");

            NextRequest();
        }

        protected abstract void OnDeviceReady(BluetoothDevice device);

        protected abstract void OnStateChanged(State state);

        protected override void OnDeviceDisconnected(BluetoothDevice device)
        {
            if (CurrentRequest != null && CurrentRequest.RequestType == BluetoothRequestType.Disconnect)
                CurrentRequest.Resolve(null);
            else
                CurrentRequest?.Reject(Error.LinkLost);

            CurrentRequest = null;

            // timer to Timeout
            if (_timer != null) _timer.Enabled = false;
            _timer = null;

            _operationInProgress = true; // no more calls are possible
            _taskQueue.Clear();
            OnStateChanged(State.Disconnected);
        }

        protected override void OnDeviceConnecting(BluetoothDevice device)
        {
            _taskQueue.Clear();
            OnStateChanged(State.Connecting);
        }

        protected override void OnDeviceConnected(BluetoothDevice device)
        {
            _taskQueue.Clear();
            OnStateChanged(State.Connected);
        }

        protected override void OnDeviceDisconnecting(BluetoothDevice device)
        {
            _taskQueue.Clear();
            OnStateChanged(State.Disconnecting);
        }

        protected override void OnBondingRequiered(BluetoothDevice device)
        {
        }

        protected override void OnBonded(BluetoothDevice device)
        {
        }

        protected override void OnBatteryValueReceived(int value)
        {
        }

        protected override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
//            Logger?.v("OnCharacteristicRead \n");
            FinishCurrentRequest(characteristic.GetValue());
        }

        protected override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
//            Logger?.v("OnCharacteristicWrite \n");
            FinishCurrentRequest(characteristic.GetValue());
        }

        protected override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor)
        {
//            Logger?.d("OnDescriptorWrite \n");
            FinishCurrentRequest(descriptor.GetValue());
        }

        protected override void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor)
        {
//            Logger?.v("OnDescriptorRead \n");
            FinishCurrentRequest(descriptor.GetValue());
        }

        protected override void OnNotificationsEnabled(BluetoothGatt gatt,
            BluetoothGattCharacteristic descriptorCharacteristic)
        {
//            Logger?.v("Notifications enabled \n");
            FinishCurrentRequest();
        }

        protected override void OnIndicationsEnabled(BluetoothGatt gatt,
            BluetoothGattCharacteristic descriptorCharacteristic)
        {
//            Logger?.v("Indications enabled \n");
            FinishCurrentRequest();
        }

        protected override void OnError(BluetoothDevice device, Error errorType, GattStatus status)
        {
            if (CurrentRequest != null && CurrentRequest.IncrementRetryCount() < 3) // Task failed
            {
//                Logger?.d("Retrying failed request!");
                _taskQueue.AddFirst(CurrentRequest); // put the request back in the stack
            }
            else // throw error if an operation was running
            {
                CurrentRequest?.Reject(errorType);
                CurrentRequest = null;
            }

            if (_timer != null) _timer.Enabled = false;

            _operationInProgress = false;

            NextRequest();
        }

        private void FinishCurrentRequest(byte[] data = null)
        {
            // timer to Timeout
            if (_timer != null) _timer.Enabled = false;

            if (CurrentRequest == null) return;

//            if (data != null && CurrentRequest?.RequestType == BluetoothRequestType.Write)
//                Logger?.d($"written {BitConverter.ToString(data)}");
//            else if (data != null && CurrentRequest?.RequestType == BluetoothRequestType.Read)
//                Logger?.d($"read {BitConverter.ToString(data)}");
            CurrentRequest?.Resolve(data);
            CurrentRequest = null;
            _operationInProgress = false;


            NextRequest();
        }

        #endregion
    }
}