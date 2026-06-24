using UnityEngine;

namespace Core
{
    public class BaseState
    {
        [HideInInspector] public StateMachine sm;
        [SerializeField] public string stateName = "Base State";
        public float duration = 0;
        public float age = float.MaxValue;
        public float elapsedTime;
        public BaseState(StateMachine sm)
        {
            this.sm = sm;
            stateName = GetType().Name;
        }
        public virtual void Awake()
        {
        }
        public virtual void OnEnter()
        {
            if (duration != 0)
                age = duration;
        }
        public virtual void Update()
        {
            elapsedTime += Time.deltaTime;
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
                    BaseState tempState = sm.bufferedState;
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
    }
}