using Core;
using FMODUnity;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRuntime.Managers
{
    public static class AudioEventIds
    {
        public const string GlobalMainBgm = "GLOBAL_MAIN_BGM";
        public const string GlobalLastMinuteBgm = "GLOBAL_LAST_MINUTE_BGM";
        public const string PlayerReloadGun = "PLAYER_RELOAD_GUN";
        public const string PlayerFootstepSfx = "PLAYER_FOOTSTEP_SFX";
        public const string PlayerShootSfx = "PLAYER_SHOOT_SFX";
        public const string NemesisPunchSfx = "NEMESIS_PUNCH_SFX";
        public const string NemesisLungeSfx = "NEMESIS_LUNGE_SFX";
        public const string NemesisGroundSlamSfx = "NEMESIS_GROUND_SLAM_SFX";
        public const string ZombieWalkSfx = "ZOMBIE_WALK_SFX";
        public const string ZombieAttackSfx = "ZOMBIE_ATTACK_SFX";
        public const string ZombieCreeperExplodeSfx = "ZOMBIE_CREEPER_EXPLODE_SFX";
        public const string TurretShootSfx = "TURRET_SHOOT_SFX";
        public const string BlowdartShootSfx = "BLOWDART_SHOOT_SFX";
        public const string FlashbangSfx = "FLASHBANG_SFX";
        public const string CardSpawnSfx = "CARD_SPAWN_SFX";
        public const string CrystalShootSfx = "CRYSTAL_SHOOT_SFX";
        public const string CrystalDestroySfx = "CRYSTAL_DESTROY_SFX";
        public const string CrystalAllDownedSfx = "CRYSTAL_ALL_DOWNED_SFX";
        public const string UiCardHoverSfx = "UI_CARD_HOVER_SFX";
        public const string UiCardClickSfx = "UI_CARD_CLICK_SFX";
        public const string UiCardCancelSfx = "UI_CARD_CANCEL_SFX";
    }

    public class AudioManager : NetworkSingleton<AudioManager>
    {
        private const int InvalidLoopHandle = 0;

        [field: SerializeField, Header("Pool")]
        private int InitialPoolSize { get; set; } = 16;

        [field: SerializeField]
        private bool ExpandPoolWhenFull { get; set; } = true;

        [field: SerializeField]
        private string PoolObjectNamePrefix { get; set; } = "Audio Source";

        [field: SerializeField, Header("Playback")]
        private bool NonRigidbodyVelocity { get; set; }

        [field: SerializeField]
        private bool DefaultAllowFadeout { get; set; } = true;

        private readonly List<PooledAudioSource> _pool = new();
        private readonly List<PooledAudioSource> _pendingRelease = new();
        private readonly Dictionary<int, PooledAudioSource> _activeLoopsByHandle = new();

        private int _nextLoopHandle = 1;
        private int _bgmLoopHandle = InvalidLoopHandle;
        private string _activeBgmEventId = string.Empty;

        private void Awake()
        {
            this.Startup(this);
            this.InitializePool();
            this.UpdateSceneBgm();
        }

        private void Update()
        {
            this.UpdateSceneBgm();
            this.UpdateActiveSources();
        }

        private void OnDestroy()
        {
            this.StopCurrentBgm(false);
            this.StopAllLoops(false);
            this.ReleaseAllSources();

            if (Instance == this)
            {
                this.DestroyInstance();
            }
        }

        public void PlayOneShot(string eventId)
        {
            this.TryPlayOneShot(eventId, this.transform.position, null);
        }

        public void PlayOneShot(string eventId, Vector3 position)
        {
            this.TryPlayOneShot(eventId, position, null);
        }

        public void PlayOneShot(string eventId, Transform followTarget)
        {
            var position = followTarget != null ? followTarget.position : this.transform.position;
            this.TryPlayOneShot(eventId, position, followTarget);
        }

        public bool TryPlayOneShot(string eventId, Vector3 position)
        {
            return this.TryPlayOneShot(eventId, position, null);
        }

        public bool TryPlayOneShot(string eventId, Transform followTarget)
        {
            var position = followTarget != null ? followTarget.position : this.transform.position;
            return this.TryPlayOneShot(eventId, position, followTarget);
        }

        public bool TryPlayOneShot(string eventId, Vector3 position, Transform followTarget)
        {
            if (!this.TryGetAudioData(eventId, out var audioData))
            {
                return false;
            }

            if (audioData.PlaybackType == AudioPlaybackType.PLAYBACK_LOOP)
            {
                Debug.LogWarning($"Audio {eventId} is marked as {audioData.PlaybackType} but was played as a one-shot.");
            }

            if (!this.TryPrepareSource(audioData, position, followTarget, false, out var source))
            {
                return false;
            }

            source.Instance.start();
            return true;
        }

        public int StartLoop(string eventId)
        {
            return this.StartLoop(eventId, this.transform.position, null);
        }

        public int StartLoop(string eventId, Vector3 position)
        {
            return this.StartLoop(eventId, position, null);
        }

        public int StartLoop(string eventId, Transform followTarget)
        {
            var position = followTarget != null ? followTarget.position : this.transform.position;
            return this.StartLoop(eventId, position, followTarget);
        }

        public int StartLoop(string eventId, Vector3 position, Transform followTarget)
        {
            if (!this.TryGetAudioData(eventId, out var audioData))
            {
                return InvalidLoopHandle;
            }

            if (audioData.PlaybackType != AudioPlaybackType.PLAYBACK_LOOP)
            {
                Debug.LogWarning($"Audio {eventId} is marked as {audioData.PlaybackType} but was started as a loop.");
            }

            if (!this.TryPrepareSource(audioData, position, followTarget, true, out var source))
            {
                return InvalidLoopHandle;
            }

            var handle = this.GetNextLoopHandle();
            source.LoopHandle = handle;
            this._activeLoopsByHandle.Add(handle, source);

            source.Instance.start();
            return handle;
        }

        public bool StopLoop(int loopHandle)
        {
            return this.StopLoop(loopHandle, this.DefaultAllowFadeout);
        }

        public bool StopLoop(int loopHandle, bool allowFadeout)
        {
            if (!this._activeLoopsByHandle.TryGetValue(loopHandle, out var source))
            {
                return false;
            }

            this.StopSource(source, allowFadeout);
            return true;
        }

        public void StopLoop(string eventId)
        {
            this.StopLoops(eventId, this.DefaultAllowFadeout);
        }

        public void StopLoops(string eventId)
        {
            this.StopLoops(eventId, this.DefaultAllowFadeout);
        }

        public void StopLoops(string eventId, bool allowFadeout)
        {
            var handlesToStop = new List<int>();
            foreach (var activeLoop in this._activeLoopsByHandle)
            {
                if (activeLoop.Value.EventId == eventId)
                {
                    handlesToStop.Add(activeLoop.Key);
                }
            }

            foreach (var loopHandle in handlesToStop)
            {
                this.StopLoop(loopHandle, allowFadeout);
            }
        }

        public void StopAllLoops()
        {
            this.StopAllLoops(this.DefaultAllowFadeout);
        }

        public void StopAllLoops(bool allowFadeout)
        {
            var handlesToStop = new List<int>(this._activeLoopsByHandle.Keys);
            foreach (var loopHandle in handlesToStop)
            {
                this.StopLoop(loopHandle, allowFadeout);
            }
        }

        private void UpdateSceneBgm()
        {
            var targetBgmEventId = this.GetTargetBgmEventId();
            if (this._activeBgmEventId == targetBgmEventId)
            {
                return;
            }

            this.StopCurrentBgm();

            if (string.IsNullOrEmpty(targetBgmEventId))
            {
                return;
            }

            this._bgmLoopHandle = this.StartLoop(targetBgmEventId);
            if (this._bgmLoopHandle != InvalidLoopHandle)
            {
                this._activeBgmEventId = targetBgmEventId;
            }
        }

        private string GetTargetBgmEventId()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "ScMain")
            {
                return AudioEventIds.GlobalMainBgm;
            }

            if (sceneName != "ScGame")
            {
                return string.Empty;
            }

            var battleManager = BattleManager.Instance;
            return battleManager != null
                && battleManager.RemainingRoundSeconds <= 60
                && (
                    battleManager.RemainingRoundSeconds > 0
                    || this._activeBgmEventId == AudioEventIds.GlobalLastMinuteBgm
                )
                ? AudioEventIds.GlobalLastMinuteBgm
                : AudioEventIds.GlobalMainBgm;
        }

        private void StopCurrentBgm(bool allowFadeout = true)
        {
            if (this._bgmLoopHandle == InvalidLoopHandle)
            {
                this._activeBgmEventId = string.Empty;
                return;
            }

            this.StopLoop(this._bgmLoopHandle, allowFadeout);
            this._bgmLoopHandle = InvalidLoopHandle;
            this._activeBgmEventId = string.Empty;
        }

        private void InitializePool()
        {
            if (this._pool.Count > 0)
            {
                return;
            }

            for (var i = 0; i < this.transform.childCount; i++)
            {
                this.RegisterPoolObject(this.transform.GetChild(i).gameObject);
            }

            while (this._pool.Count < this.InitialPoolSize)
            {
                this.CreatePoolObject();
            }
        }

        private PooledAudioSource CreatePoolObject()
        {
            var poolObject = new GameObject($"{this.PoolObjectNamePrefix} {this._pool.Count + 1:00}");
            poolObject.transform.SetParent(this.transform, false);
            return this.RegisterPoolObject(poolObject);
        }

        private PooledAudioSource RegisterPoolObject(GameObject poolObject)
        {
            poolObject.name = $"{this.PoolObjectNamePrefix} {this._pool.Count + 1:00}";
            poolObject.transform.SetParent(this.transform, false);
            poolObject.SetActive(false);

            var source = new PooledAudioSource(poolObject);
            this._pool.Add(source);
            return source;
        }

        private bool TryPrepareSource(AudioData audioData, Vector3 position, Transform followTarget, bool isLooping, out PooledAudioSource source)
        {
            source = this.ClaimSource();
            if (source == null)
            {
                Debug.LogWarning($"No pooled audio source available for {audioData.EventId}.");
                return false;
            }

            if (!this.TryCreateEventInstance(audioData, out var eventInstance))
            {
                this.ReleaseSource(source);
                return false;
            }

            source.IsBusy = true;
            source.IsLooping = isLooping;
            source.IsStopping = false;
            source.EventId = audioData.EventId;
            source.AudioData = audioData;
            source.FollowTarget = audioData.SpatialMode == AudioSpatialMode.SPATIAL_FOLLOW3D ? followTarget : null;
            source.Instance = eventInstance;
            source.GameObject.SetActive(true);

            this.PlaceSource(source, audioData, position, followTarget);

            if (audioData.SpatialMode != AudioSpatialMode.SPATIAL_2D)
            {
                RuntimeManager.AttachInstanceToGameObject(source.Instance, source.GameObject, this.NonRigidbodyVelocity);
            }

            return true;
        }

        private PooledAudioSource ClaimSource()
        {
            foreach (var source in this._pool)
            {
                if (!source.IsBusy)
                {
                    return source;
                }
            }

            return this.ExpandPoolWhenFull ? this.CreatePoolObject() : null;
        }

        private bool TryGetAudioData(string eventId, out AudioData audioData)
        {
            audioData = default;

            if (string.IsNullOrEmpty(eventId))
            {
                Debug.LogWarning("Cannot play audio with an empty event id.");
                return false;
            }

            var data = DAudio.GetDataById(eventId);
            if (!data.HasValue)
            {
                Debug.LogWarning($"Audio data not found for event id {eventId}.");
                return false;
            }

            audioData = data.Value;
            return true;
        }

        private bool TryCreateEventInstance(AudioData audioData, out FMOD.Studio.EventInstance eventInstance)
        {
            eventInstance = default;

            var eventReference = audioData.EventReference;
            if (eventReference.IsNull && !string.IsNullOrEmpty(audioData.EventPath))
            {
                eventReference = RuntimeManager.PathToEventReference(audioData.EventPath);
            }

            if (eventReference.IsNull)
            {
                Debug.LogWarning($"Audio {audioData.EventId} does not have a valid FMOD event reference.");
                return false;
            }

            try
            {
                eventInstance = RuntimeManager.CreateInstance(eventReference);
                return eventInstance.isValid();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to create FMOD event instance for {audioData.EventId}: {exception.Message}");
                return false;
            }
        }

        private void PlaceSource(PooledAudioSource source, AudioData audioData, Vector3 position, Transform followTarget)
        {
            if (audioData.SpatialMode == AudioSpatialMode.SPATIAL_2D)
            {
                source.Transform.localPosition = source.OriginalLocalPosition;
                source.Transform.localRotation = source.OriginalLocalRotation;
                return;
            }

            source.Transform.position = followTarget != null ? followTarget.position : position;
            source.Transform.rotation = Quaternion.identity;
        }

        private void UpdateActiveSources()
        {
            this._pendingRelease.Clear();

            foreach (var source in this._pool)
            {
                if (!source.IsBusy)
                {
                    continue;
                }

                if (source.FollowTarget != null)
                {
                    source.Transform.position = source.FollowTarget.position;
                }

                if (!source.Instance.isValid() || this.IsStopped(source.Instance))
                {
                    this._pendingRelease.Add(source);
                }
            }

            foreach (var source in this._pendingRelease)
            {
                this.ReleaseSource(source);
            }
        }

        private bool IsStopped(FMOD.Studio.EventInstance eventInstance)
        {
            eventInstance.getPlaybackState(out var playbackState);
            return playbackState == FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }

        private void StopSource(PooledAudioSource source, bool allowFadeout)
        {
            if (!source.IsBusy)
            {
                return;
            }

            if (source.Instance.isValid())
            {
                source.Instance.stop(allowFadeout
                    ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
                    : FMOD.Studio.STOP_MODE.IMMEDIATE);
            }

            source.IsStopping = true;

            if (!allowFadeout)
            {
                this.ReleaseSource(source);
            }
        }

        private void ReleaseAllSources()
        {
            foreach (var source in this._pool)
            {
                this.ReleaseSource(source);
            }
        }

        private void ReleaseSource(PooledAudioSource source)
        {
            if (!source.IsBusy)
            {
                return;
            }

            if (source.LoopHandle != InvalidLoopHandle)
            {
                this._activeLoopsByHandle.Remove(source.LoopHandle);
            }

            if (source.Instance.isValid())
            {
                RuntimeManager.DetachInstanceFromGameObject(source.Instance);
                source.Instance.release();
                source.Instance.clearHandle();
            }

            source.IsBusy = false;
            source.IsLooping = false;
            source.IsStopping = false;
            source.EventId = string.Empty;
            source.FollowTarget = null;
            source.LoopHandle = InvalidLoopHandle;
            source.AudioData = default;
            source.Transform.SetParent(this.transform, false);
            source.Transform.localPosition = source.OriginalLocalPosition;
            source.Transform.localRotation = source.OriginalLocalRotation;
            source.Transform.localScale = source.OriginalLocalScale;
            source.GameObject.SetActive(false);
        }

        private int GetNextLoopHandle()
        {
            if (this._nextLoopHandle == int.MaxValue)
            {
                this._nextLoopHandle = 1;
            }

            while (this._activeLoopsByHandle.ContainsKey(this._nextLoopHandle))
            {
                this._nextLoopHandle++;
            }

            return this._nextLoopHandle++;
        }

        private class PooledAudioSource
        {
            public readonly GameObject GameObject;
            public readonly Transform Transform;
            public readonly Vector3 OriginalLocalPosition;
            public readonly Quaternion OriginalLocalRotation;
            public readonly Vector3 OriginalLocalScale;

            public AudioData AudioData;
            public FMOD.Studio.EventInstance Instance;
            public Transform FollowTarget;
            public string EventId = string.Empty;
            public int LoopHandle;
            public bool IsBusy;
            public bool IsLooping;
            public bool IsStopping;

            public PooledAudioSource(GameObject gameObject)
            {
                this.GameObject = gameObject;
                this.Transform = gameObject.transform;
                this.OriginalLocalPosition = this.Transform.localPosition;
                this.OriginalLocalRotation = this.Transform.localRotation;
                this.OriginalLocalScale = this.Transform.localScale;
            }
        }
    }
}
