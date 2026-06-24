using UnityEngine;

namespace Core
{
    public class StateMachine : MonoBehaviour
    {
        public bool logStateTransfer = false;
        public virtual BaseState StartState => new BaseState(this);
        public virtual BaseState DefaultState => new BaseState(this);
        public BaseState currentState;
        /// <summary>
        /// Only used for QueueState, will not be used if ChangeState is called directly
        /// </summary>
        public BaseState nextState;
        public BaseState bufferedState;

        public System.Action OnDeath; // Should be called before OnDestroy since destroying objects gets rid of references

        protected virtual void Awake()
        {
        }
        protected virtual void Start()
        {
            Init();
        }
        protected virtual void Update()
        {
            currentState?.Update();
        }
        protected virtual void FixedUpdate()
        {
            currentState?.FixedUpdate();
            if (nextState != null)
                ChangeState(nextState);
        }
        public virtual void ChangeState(BaseState newState)
        {
            currentState?.OnExit();
            nextState = null;
            if (logStateTransfer)
                print($"Before: {currentState.stateName}, after: {newState.stateName}");
            newState.OnEnter();
            currentState = newState;
        }
        /// <summary>
        /// <para>Only the first queued state will be used, any consequent queuestates will be discarded.</para>
        /// <para>This exists to ensure that multiple state changes do not occur in a single fixed Update, especially using inheritance.</para>
        /// </summary>
        /// <param name="newState"></param>
        public virtual void QueueState(BaseState newState)
        {
            if (nextState != null)
            {
                if (logStateTransfer)
                    print($"DISCARDED {newState.stateName} for {nextState.stateName}");
                return;
            }
            nextState = newState;
        }

        public virtual void Init()
        {
            if (StartState == null) return;
            currentState = StartState;
            currentState.OnEnter();
        }
    }
}