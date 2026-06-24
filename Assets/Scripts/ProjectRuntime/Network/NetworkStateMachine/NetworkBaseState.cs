using Mirror;
using System;
using System.Linq;
using UnityEngine;

namespace ProjectRuntime.Network
{
    public class NetworkBaseState : IEquatable<NetworkBaseState>
    {
        [HideInInspector] public NetworkStateMachine sm;
        [SerializeField] public string stateName = "Base State";
        public float duration = 0;
        public float age = float.MaxValue;
        public float elapsedTime;
        public int StateId => GameNetworkManager.Instance.State2GuidCache[GetType()];
        public uint serverTick;
        public NetworkBaseState(NetworkStateMachine sm)
        {
            this.sm = sm;
            stateName = GetType().Name;
        }
        public virtual void OnSerialize(NetworkWriter writer)
        {
            writer.Write(duration);
            writer.Write(age);
            writer.Write(elapsedTime);
        }
        public virtual void OnDeserialize(NetworkReader reader)
        {
            duration = reader.ReadFloat();
            age = reader.ReadFloat();
            elapsedTime = reader.ReadFloat();
        }
        public void ApplyTickOffset()
        {
            // Chain continuation: incoming state matches what the dummy client expected next
            bool isChainContinuation = !sm.authority && sm.currentState != null
                && (Equals(sm.bufferedState) || Equals(sm.nextState));

            float timeOffset;
            if (isChainContinuation)
            {
                timeOffset = sm.chainBudget;
            }
            else
            {
                // Fresh chain start: compute from network latency
                uint tickDifference = 0;
                if (serverTick < NetworkTick.Current)
                    tickDifference = NetworkTick.Current - serverTick;
                timeOffset = (float)(NetworkTick.ToDeltaTime(tickDifference) / 2);
            }

            age -= timeOffset;
            elapsedTime += timeOffset;

            // Pass the remainder forward to the next state in the chain
            sm.chainBudget = Mathf.Max(0f, timeOffset - duration);

            if (sm.logStateTransfer)
                Debug.Log($"{serverTick}: Added timeOffset of {timeOffset}/{duration} to {sm.name} {stateName}");
        }

        public virtual void Awake()
        {
        }
        public virtual void OnEnter()
        {
            if (duration != 0 && sm.authority)
            {
                age = duration;
                elapsedTime = 0;
            }
        }
        public virtual void Update()
        {
            elapsedTime += Time.deltaTime;
        }
        public virtual void LateUpdate()
        {
        }
        /// <summary>
        /// <para>Counts down the age of the state if a duration is set.</para>
        /// This will <see cref="StateMachine.QueueState(BaseState)">QueueState</see> to buffered or default state after this state's duration expires.
        /// </summary>
        public virtual void FixedUpdate()
        {
            StateUpdate();
            if (duration == 0) return;
            age -= Time.fixedDeltaTime;
            if (age <= 0)
            {
                OnStateExpired();
                if (sm.bufferedState == null)
                    sm.QueueState(sm.DefaultState);
                else
                {
                    NetworkBaseState tempState = sm.bufferedState;
                    sm.bufferedState = null;
                    sm.QueueState(tempState);
                }
            }
        }
        /// <summary>
        /// <para>FixedUpdate will queue <see cref="StateMachine.DefaultState">DefaultState</see> or <see cref="StateMachine.bufferedState">bufferedState</see> on low priority.</para>
        /// Should only handle state management. Calling <see cref="StateMachine.QueueState(BaseState)">QueueState</see> should only be done here, <see cref="StateMachine.bufferedState">bufferedState</see> can be set anywhere in the state.
        /// </summary>
        public virtual void StateUpdate()
        {
        }
        public virtual void OnExit()
        {
        }
        public virtual void OnStateExpired()
        {
        }
        public bool Equals(NetworkBaseState other)
        {
            if (other == null) return false;
            if (GetType() != other.GetType()) return false;

            //Compare if the TIME is too far off from the other state in a timed state (Sya like 1 second

            return true;
        }
    }
}