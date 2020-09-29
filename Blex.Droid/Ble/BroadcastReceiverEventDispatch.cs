using System;
using Android.Content;

namespace Blex.Droid.Ble
{
    public class BroadcastReceiverEventDispatch : BroadcastReceiver
    {
        public event EventHandler<Intent> IntentReceived;

        public override void OnReceive(Context context, Intent intent)
        {
            IntentReceived?.Invoke(this, intent);
        }
    }
}