using Mirror;
using System.Linq;
using System;
using UnityEngine;
using System.Collections.Generic;
using ProjectRuntime.Network;

namespace ProjectRuntime.Network
{
    public struct NetworkBaseStateSerialized
    {
        public byte[] bytes;
    }

    public static class NetworkTick
    {
        // Uses Mirror's synchronized server time so tick numbers are consistent across all peers.
        public static uint Current => (uint)(NetworkTime.time / Time.fixedDeltaTime);
        public static double ToDeltaTime(uint tickDelta) => tickDelta * Time.fixedDeltaTime;
    }

    public class NetworkStateMachine : NetworkBehaviour
    {
        public bool logStateTransfer = false;
        public virtual NetworkBaseState StartState => new NetworkBaseState(this);
        public virtual NetworkBaseState DefaultState => new NetworkBaseState(this);
        public NetworkBaseState currentState;
        /// <summary>
        /// Only used for QueueState, will not be used if ChangeState is called directly
        /// </summary>
        public NetworkBaseState nextState;
        public NetworkBaseState bufferedState;
        [ReadOnly] public float chainBudget;

        public System.Action OnDeath; // Should be called before OnDestroy since destroying objects gets rid of references

        protected virtual void Awake()
        {
        }
        protected virtual void Start()
        {
        }
        public virtual void NetworkStart()
        {
            GameNetworkManager.Instance.RegisterSM(netId, this);
            if (StartState == null) return;

            if (authority)
                ChangeState(StartState);
        }
        public override void OnStartServer()
        {
            base.OnStartServer();
            if (!isClient) NetworkStart(); // host calls OnStartClient too, so skip here
        }
        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkStart();
        }
        protected virtual void Update()
        {
            currentState?.Update();
        }
        protected virtual void LateUpdate()
        {
            currentState?.LateUpdate();
        }
        protected virtual void FixedUpdate()
        {
            currentState?.FixedUpdate();
            if (nextState != null)
                ChangeState(nextState);
        }
        public virtual void ChangeState(NetworkBaseState newState)
        {
            //Debug.Log($"{netId} changed state to {newState.stateName} with authority {authority}");
            if (authority)
            {
                chainBudget = 0;
                newState.serverTick = NetworkTick.Current;
                currentState?.OnExit();
                nextState = null;
                if (logStateTransfer)
                    print($"Before: {currentState?.stateName}, after: {newState.stateName}");
                newState.OnEnter();
                currentState = newState;

                if (isServer)
                    RpcApplyState(newState);
                else
                    CmdApplyState(newState);
            }
        }

        /// <summary>
        /// <para>Only the first queued state will be used, any consequent queuestates will be discarded.</para>
        /// <para>This exists to ensure that multiple state changes do not occur in a single fixed Update, especially using inheritance.</para>
        /// </summary>
        /// <param name="newState"></param>
        public virtual void QueueState(NetworkBaseState newState)
        {
            if (nextState != null)
            {
                if (logStateTransfer)
                    print($"DISCARDED {newState.stateName} for {nextState.stateName}");
                return;
            }
            nextState = newState;
        }

        [Command]
        public void CmdApplyState(NetworkBaseState newState)
        {
            RpcApplyState(newState);
            ServerApplyState(newState);
        }
        [ClientRpc(includeOwner = false)]
        public void RpcApplyState(NetworkBaseState newState) // OnDeserialize runs here
        {
            if (isServer) return; // state already applied on host, avoid double calling
            ApplyRemoteState(newState);
        }

        [TargetRpc]
        public void TargetApplyState(NetworkConnectionToClient target, NetworkBaseState newState)
        {
            if (isServer) return; // state already applied on host, avoid double calling
            ApplyRemoteState(newState);
        }

        [Server]
        public void ServerApplyState(NetworkBaseState newState) // OnDeserialize runs here
        {
            currentState?.OnExit();
            newState.ApplyTickOffset();
            nextState = null;
            if (logStateTransfer)
                print($"DummyClient: Before: {currentState?.stateName}, after: {newState.stateName}");
            newState.OnEnter();
            currentState = newState;
        }

        [Server]
        public void ServerForceState(NetworkBaseState newState)
        {
            chainBudget = 0;
            newState.serverTick = NetworkTick.Current;
            ServerApplyState(newState);
            RpcApplyState(newState);

            if (connectionToClient != null)
            {
                TargetApplyState(connectionToClient, newState);
            }
        }

        private void ApplyRemoteState(NetworkBaseState newState)
        {
            if (currentState != null && newState.serverTick < currentState.serverTick) // Reject stale corrections - a later tick is already running locally
                return;
            currentState?.OnExit();
            newState.ApplyTickOffset();
            nextState = null;
            if (logStateTransfer)
                print($"DummyClient: Before: {currentState?.stateName}, after: {newState.stateName}");
            newState.OnEnter();
            currentState = newState;
        }
    }

    public static class NetworkStateMachineExtensions
    {
        public static void OnSerialize(this NetworkWriter writer, NetworkBaseState state)
        {
            writer.Write(state.StateId);
            writer.Write(state.sm.netId);
            writer.WriteInt(GameNetworkManager.SMTypeHash(state.sm.GetType())); // disambiguate when multiple SMs share a netId
            writer.WriteUInt(state.serverTick);
            state.OnSerialize(writer);
        }
        public static NetworkBaseState OnDeserialize(this NetworkReader reader)
        {
            // Must match weaver's preferred encoding: VarInt/VarUInt (not fixed 4-byte ReadInt/ReadUInt)
            var stateId = reader.ReadVarInt();
            var smNetId = reader.ReadVarUInt(); // Cant read/write NetworkStateMachine because its not a final class
            var smTypeHash = reader.ReadInt();
            var sm = GameNetworkManager.Instance.NetId2SM[(smNetId, smTypeHash)];

            var stateToSpawn = GameNetworkManager.Instance.Guid2StateCache[stateId];
            var newState = (NetworkBaseState)Activator.CreateInstance(stateToSpawn, args: new object[] { sm });
            newState.serverTick = reader.ReadUInt();
            newState.OnDeserialize(reader);
            return newState;
        }
    }
}
