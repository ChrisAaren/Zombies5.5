using System;
using UnityEngine;

namespace Zombies55
{
    // Two weapon slots, hitscan firing, reload, aim and buying (Give / RefillAmmo).
    // Input comes from GameInput (rebindable). Defaults: LMB fire, RMB aim, R reload, 1/2 or wheel swap; gamepad RT, LT, X, Y.
    public class PlayerWeapons : MonoBehaviour
    {
        public const int SlotCount = 2;

        // The WeaponSwap clip is 0.47 s (14 frames at 30 fps); the model changes at frame 6.
        const float SwapSeconds = 0.47f;
        const float FireBufferSeconds = 0.12f;

        public static PlayerWeapons Instance { get; private set; }

        public WeaponDefinition Current => GetSlot(ActiveSlot);
        public int ActiveSlot { get; private set; }
        public int Magazine { get; private set; }            // rounds in the current gun
        public int Reserve { get; private set; }             // spare rounds for the current gun
        public bool IsReloading { get; private set; }
        public float ReloadProgress { get; private set; }    // 0..1 while reloading
        public bool IsAiming { get; private set; }
        public float CurrentSpreadDegrees { get; private set; }

        // Extras for the rest of the combat layer.
        public float LastFireTime { get; private set; } = -999f;
        public bool IsSwapping => Time.time < _swapEnd;
        // Pump shotgun tube load (when its clips exist): shells go in one at a time and firing interrupts the reload.
        public bool IsTubeLoading => IsReloading && _tube;
        public bool TubeMoreShells => _tubeMore;
        public float TubeSpeed => _tubeSpeed;
        public float AimBlend => _aimBlend;

#pragma warning disable CS0067 // raised by the implementation
        public event Action WeaponChanged;                   // slot switch, buy, replace
        public event Action AmmoChanged;
        public event Action Fired;
        public event Action<bool, bool> HitConfirmed;        // (killed, headshot)
#pragma warning restore CS0067

        readonly WeaponDefinition[] _slots = new WeaponDefinition[SlotCount];
        readonly int[] _mag = new int[SlotCount];
        readonly int[] _res = new int[SlotCount];

        PlayerController _player;
        PlayerAnimation _pose;
        CameraOrbit _orbit;
        float _nextFireTime;
        float _swapEnd;
        float _fireBufferUntil;
        float _reloadStart;
        float _reloadDuration;
        int _reloadStage;
        bool _tube, _tubeMore;
        int _tubeStage, _tubeShellsLeft;
        float _tubeSpeed = 1f, _tubeStageStart, _tubeTotal, _tubeElapsedBase;
        float _tubeStartLen, _tubeShellLen, _tubeEndLen;
        float _bloom;
        float _lastShotTime = -999f;
        float _aimBlend;
        float _nextDryTime;
        bool _aimSent;
        bool _inBurst;
        int _burstLeft;
        float _boltSoundAt = -1f;
        static readonly RaycastHit[] s_Hits = new RaycastHit[32];
        static readonly System.Collections.Generic.HashSet<IDamageable> s_Seen = new System.Collections.Generic.HashSet<IDamageable>();
        static readonly System.Collections.Generic.IComparer<RaycastHit> s_ByDistance =
            System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        void Awake()
        {
            Instance = this;
            _player = GetComponent<PlayerController>();
            _pose = GetComponentInChildren<PlayerAnimation>();
            _slots[0] = WeaponCatalog.Get(WeaponCatalog.StarterId);
            _mag[0] = _slots[0].MagazineSize;
            _res[0] = _slots[0].MaxReserve;
            Magazine = _mag[0];
            Reserve = _res[0];
        }

        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            WeaponChanged?.Invoke();
            AmmoChanged?.Invoke();
        }

        // ------------------------------------------------------------------ public API

        public WeaponDefinition GetSlot(int slot) => slot >= 0 && slot < SlotCount ? _slots[slot] : null;

        public bool Owns(string weaponId)
        {
            for (int i = 0; i < SlotCount; i++) if (_slots[i] != null && _slots[i].Id == weaponId) return true;
            return false;
        }

        // True when the gun is owned and both magazine and reserve are full.
        public bool IsAmmoFull(string weaponId)
        {
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] != null && _slots[i].Id == weaponId)
                    return _mag[i] >= _slots[i].MagazineSize && _res[i] >= _slots[i].MaxReserve;
            return false;
        }

        // Fills its ammo; takes an empty slot first, otherwise replaces the active slot. Switches to it.
        public void Give(WeaponDefinition weapon)
        {
            if (weapon == null) return;
            int slot = -1;
            for (int i = 0; i < SlotCount; i++) if (_slots[i] != null && _slots[i].Id == weapon.Id) { slot = i; break; }
            if (slot < 0) for (int i = 0; i < SlotCount; i++) if (_slots[i] == null) { slot = i; break; }
            if (slot < 0) slot = ActiveSlot;

            _slots[slot] = weapon;
            _mag[slot] = weapon.MagazineSize;
            _res[slot] = weapon.MaxReserve;
            SwitchTo(slot, true);
        }

        public void RefillAmmo(string weaponId)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null || _slots[i].Id != weaponId) continue;
                _mag[i] = _slots[i].MagazineSize;
                _res[i] = _slots[i].MaxReserve;
                if (i == ActiveSlot)
                {
                    IsReloading = false;
                    _tube = false;
                    ReloadProgress = 0f;
                    SyncAmmo();
                }
            }
        }

        // Ammo Cache pickup: magazine and reserve of every owned gun back to full.
        public void RefillAllAmmo()
        {
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i] != null) RefillAmmo(_slots[i].Id);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            bool active = (GameLoop.Instance == null || GameLoop.Instance.State == RunState.Running) && !PlayerRagdoll.IsDown;
            var def = Current;

            // Aim
            bool wantAim = active && def != null && GameInput.IsPressed(GameAction.Aim);
            IsAiming = wantAim;
            _aimBlend = Mathf.MoveTowards(_aimBlend, wantAim ? 1f : 0f, dt * 8f);
            if (wantAim != _aimSent)
            {
                _aimSent = wantAim;
                if (_orbit == null) _orbit = FindAnyObjectByType<CameraOrbit>();
                if (_orbit != null) _orbit.SetAiming(wantAim);
            }

            if (active && def != null)
            {
                HandleSwapInput();
                HandleReloadInput();
                UpdateReload();
                UpdateBurstAndBolt(def);
                HandleFire(def);
            }
            else if (IsReloading && !active)
            {
                _reloadStart += dt; // time passes only while running
                _tubeStageStart += dt;
            }

            UpdateSpread(def, dt);
        }

        void HandleSwapInput()
        {
            if (GameInput.WasPressedThisFrame(GameAction.Weapon1)) TrySwap(0);
            if (GameInput.WasPressedThisFrame(GameAction.Weapon2)) TrySwap(1);
            if (GameInput.WasPressedThisFrame(GameAction.NextWeapon)) TrySwap((ActiveSlot + 1) % SlotCount);
        }

        void HandleReloadInput()
        {
            // Reload and Buy may share a control (gamepad X): holding Buy at a terminal must not also reload.
            bool atBuy = (WeaponKiosk.PlayerAtKiosk || ZoneGate.PlayerAtGate) && GameInput.IsPressed(GameAction.Interact);
            if (GameInput.WasPressedThisFrame(GameAction.Reload) && !atBuy) StartReload();
        }

        void HandleFire(WeaponDefinition def)
        {
            bool pressed = GameInput.WasPressedThisFrame(GameAction.Fire);
            bool held = GameInput.IsPressed(GameAction.Fire);
            if (pressed) _fireBufferUntil = Time.time + FireBufferSeconds;

            bool want = def.Mode == FireMode.Auto ? held : Time.time <= _fireBufferUntil;
            if (!want || _inBurst) return;
            // Firing interrupts a tube reload as soon as there is a shell in the gun.
            if (IsReloading && _tube && _mag[ActiveSlot] > 0 && !IsSwapping) CancelTubeReload();
            if (IsReloading || IsSwapping) return;
            if (_player != null && _player.IsSprinting) return;
            if (Time.time < _nextFireTime) return;

            if (_mag[ActiveSlot] <= 0)
            {
                if (_res[ActiveSlot] > 0) StartReload();
                else if (Time.time >= _nextDryTime)
                {
                    _nextDryTime = Time.time + 0.3f;
                    CombatAudio.Instance?.PlayDryFire();
                }
                _fireBufferUntil = 0f;
                return;
            }

            _fireBufferUntil = 0f;
            FireOnce(def);
        }

        // A burst in progress fires its remaining rounds at the in-burst rate (reload, swap or an empty magazine ends it);
        // a bolt action sounds its bolt at the end of the cycle that follows the shot.
        void UpdateBurstAndBolt(WeaponDefinition def)
        {
            if (_boltSoundAt > 0f && Time.time >= _boltSoundAt)
            {
                _boltSoundAt = -1f;
                if (!IsReloading && !IsSwapping) CombatAudio.Instance?.PlayBolt();
            }
            if (!_inBurst) return;
            if (def.Mode != FireMode.Burst || IsReloading || IsSwapping || _mag[ActiveSlot] <= 0)
            {
                _inBurst = false;
                _nextFireTime = Mathf.Max(_nextFireTime, Time.time + (def.Mode == FireMode.Burst ? def.BurstPause : 0f));
                return;
            }
            if (Time.time >= _nextFireTime) FireOnce(def);
        }

        // ------------------------------------------------------------------ reload and swap

        void StartReload()
        {
            var def = Current;
            if (def == null || IsReloading || IsSwapping) return;
            if (_mag[ActiveSlot] >= def.MagazineSize || _res[ActiveSlot] <= 0) return;
            IsReloading = true;
            ReloadProgress = 0f;
            _reloadStart = Time.time;
            _reloadDuration = Mathf.Max(0.2f, def.ReloadSeconds);
            _reloadStage = 0;
            _tube = false;
            _inBurst = false;
            _boltSoundAt = -1f;
            if (def.Mode == FireMode.Pump && _pose != null && _pose.TubeClipsPresent) BeginTubeReload(def);
            CombatAudio.Instance?.PlayReloadStage(0, def.Id);
        }

        void BeginTubeReload(WeaponDefinition def)
        {
            _pose.TubeClipLengths(out _tubeStartLen, out _tubeShellLen, out _tubeEndLen);
            int missing = Mathf.Min(def.MagazineSize - _mag[ActiveSlot], _res[ActiveSlot]);
            // Clip speed that lands a full tube on ReloadSeconds, within the 0.8-1.3 playback range.
            float fullClip = _tubeStartLen + _tubeEndLen + _tubeShellLen * def.MagazineSize;
            _tubeSpeed = Mathf.Clamp(fullClip / Mathf.Max(0.2f, def.ReloadSeconds), 0.8f, 1.3f);
            _tube = true;
            _tubeStage = 0;
            _tubeShellsLeft = missing;
            _tubeMore = missing > 1;
            _tubeStageStart = Time.time;
            _tubeElapsedBase = 0f;
            _tubeTotal = (_tubeStartLen + _tubeEndLen + _tubeShellLen * missing) / _tubeSpeed;
        }

        void CancelTubeReload()
        {
            IsReloading = false;
            ReloadProgress = 0f;
            _tube = false;
        }

        // Start clip, then one shell clip per shell (the round is added when its clip ends), then the pump.
        void UpdateTubeReload()
        {
            float stageLen = (_tubeStage == 0 ? _tubeStartLen : _tubeStage == 1 ? _tubeShellLen : _tubeEndLen) / _tubeSpeed;
            float done = _tubeElapsedBase + (Time.time - _tubeStageStart);
            ReloadProgress = Mathf.Clamp01(done / Mathf.Max(0.01f, _tubeTotal));
            if (Time.time - _tubeStageStart < stageLen) return;
            _tubeElapsedBase += stageLen;
            _tubeStageStart += stageLen;
            if (_tubeStage == 0)
            {
                _tubeStage = 1;
                CombatAudio.Instance?.PlayReloadStage(1, Current?.Id);
            }
            else if (_tubeStage == 1)
            {
                int slot = ActiveSlot;
                _mag[slot]++;
                _res[slot]--;
                _tubeShellsLeft--;
                SyncAmmo();
                CombatAudio.Instance?.PlayReloadStage(1, Current?.Id);
                _tubeMore = _tubeShellsLeft > 1;
                if (_tubeShellsLeft <= 0) _tubeStage = 2;
            }
            else
            {
                IsReloading = false;
                ReloadProgress = 0f;
                _tube = false;
                CombatAudio.Instance?.PlayReloadStage(2, Current?.Id);
            }
        }

        void UpdateReload()
        {
            if (!IsReloading) return;
            if (_tube) { UpdateTubeReload(); return; }
            ReloadProgress = Mathf.Clamp01((Time.time - _reloadStart) / _reloadDuration);
            if (_reloadStage == 0 && ReloadProgress > 0.5f)
            {
                _reloadStage = 1;
                CombatAudio.Instance?.PlayReloadStage(1, Current?.Id);
            }
            if (ReloadProgress < 1f) return;

            var def = Current;
            int slot = ActiveSlot;
            int take = Mathf.Min(def.MagazineSize - _mag[slot], _res[slot]);
            _mag[slot] += take;
            _res[slot] -= take;
            IsReloading = false;
            ReloadProgress = 0f;
            CombatAudio.Instance?.PlayReloadStage(2, Current?.Id);
            SyncAmmo();
        }

        void TrySwap(int slot)
        {
            if (slot == ActiveSlot || _slots[slot] == null) return;
            SwitchTo(slot, false);
        }

        void SwitchTo(int slot, bool bought)
        {
            ActiveSlot = slot;
            IsReloading = false;
            _tube = false;
            _inBurst = false;
            _boltSoundAt = -1f;
            ReloadProgress = 0f;
            _bloom = 0f;
            _fireBufferUntil = 0f;
            _swapEnd = Time.time + SwapSeconds;
            _nextFireTime = Mathf.Max(_nextFireTime, _swapEnd);
            if (!bought) CombatAudio.Instance?.PlaySwap();
            WeaponChanged?.Invoke();
            SyncAmmo();
        }

        void SyncAmmo()
        {
            Magazine = _mag[ActiveSlot];
            Reserve = _res[ActiveSlot];
            AmmoChanged?.Invoke();
        }

        // ------------------------------------------------------------------ spread

        void UpdateSpread(WeaponDefinition def, float dt)
        {
            if (def == null) { CurrentSpreadDegrees = 0f; return; }
            float maxBloom = Mathf.Max(0.3f, def.HipSpread * 0.7f);
            if (Time.time - _lastShotTime > 0.08f)
                _bloom = Mathf.MoveTowards(_bloom, 0f, dt * (0.6f + def.HipSpread * 0.7f));
            _bloom = Mathf.Min(_bloom, maxBloom);

            float baseSpread = Mathf.Lerp(def.HipSpread, def.AimSpread, _aimBlend);
            float move = 0f;
            if (_player != null)
            {
                move = Mathf.Clamp01(_player.PlanarSpeed / Mathf.Max(0.1f, _player.MaxSpeed)) * 0.9f;
                if (!_player.IsGrounded) move += 0.6f;
            }
            CurrentSpreadDegrees = baseSpread * (1f + move) + _bloom;
        }

        // ------------------------------------------------------------------ shooting

        void FireOnce(WeaponDefinition def)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Physics.SyncTransforms(); // animated hitboxes: shoot at where the enemy is drawn this frame
            int slot = ActiveSlot;
            _mag[slot]--;
            _nextFireTime = Time.time + def.SecondsPerShot;
            LastFireTime = Time.time;
            _lastShotTime = Time.time;

            Vector3 muzzlePos, muzzleFwd;
            Transform muzzle = _pose != null ? _pose.Muzzle : null;
            if (muzzle != null) { muzzlePos = muzzle.position; muzzleFwd = muzzle.forward; }
            else { muzzlePos = transform.position + Vector3.up * 1.3f + transform.forward * 0.5f; muzzleFwd = transform.forward; }

            // Shots start where the camera ray passes the player, so nothing behind the player gets hit first.
            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;
            float along = Mathf.Max(0.5f, Vector3.Dot(transform.position + Vector3.up * 1.4f - camPos, camFwd));
            Vector3 origin = camPos + camFwd * along;

            float spread = CurrentSpreadDegrees;
            bool anyHit = false, anyKill = false, anyHead = false;
            if (def.Projectile)
            {
                // The shell flies an arc that lands on the aim point; it has no tracer and no hit marker.
                Vector3 dir = ConeDirection(camFwd, cam.transform.up, spread);
                Vector3 aimPoint = Physics.Raycast(origin, dir, out RaycastHit ph, def.Range + along, GameLayers.ShotMask,
                    QueryTriggerInteraction.Ignore) ? ph.point : origin + dir * def.Range;
                var launcher = GrenadeLauncher.Instance;
                if (launcher != null) launcher.Launch(muzzlePos, aimPoint, def, gameObject);
            }
            else
            {
                for (int p = 0; p < Mathf.Max(1, def.Pellets); p++)
                {
                    Vector3 dir = ConeDirection(camFwd, cam.transform.up, spread);
                    if (def.Pierce > 1) ShootPiercing(def, origin, dir, along, muzzlePos, ref anyHit, ref anyKill, ref anyHead);
                    else ShootRay(def, origin, dir, along, muzzlePos, ref anyHit, ref anyKill, ref anyHead);
                }
            }

            // Cadence: a burst fires its rounds at the in-burst rate and then pauses; a bolt action sounds its bolt late in the cycle.
            bool firstOfBurst = true;
            if (def.Mode == FireMode.Burst)
            {
                firstOfBurst = !_inBurst;
                if (firstOfBurst) { _inBurst = true; _burstLeft = Mathf.Max(1, def.BurstCount); }
                _burstLeft--;
                _nextFireTime = Time.time + (_burstLeft > 0 ? def.SecondsPerShot : def.BurstPause);
                if (_burstLeft <= 0) _inBurst = false;
            }
            else if (def.Mode == FireMode.Bolt)
            {
                _boltSoundAt = Time.time + Mathf.Min(0.55f, def.SecondsPerShot * 0.4f);
            }

            _bloom += Mathf.Max(def.AimSpread * 0.6f, 0.15f);
            if (_orbit == null) _orbit = FindAnyObjectByType<CameraOrbit>();
            float k = IsAiming ? 0.75f : 1f;
            if (_orbit != null) _orbit.AddRecoil(def.RecoilPitch * k, UnityEngine.Random.Range(-def.RecoilYaw, def.RecoilYaw) * k);

            CombatAudio.Instance?.PlayShot(def.Id);
            if (muzzle != null) FlashFor(def, muzzle);
            if (firstOfBurst && def.Shake > 0f) CombatFX.Instance?.Shake(def.Shake * (IsAiming ? 0.8f : 1f));

            if (anyHit) HitConfirmed?.Invoke(anyKill, anyHead);
            Fired?.Invoke();
            SyncAmmo();
        }

        static void FlashFor(WeaponDefinition def, Transform muzzle)
        {
            float size, smoke = 1f;
            switch (def.Id)
            {
                case "mule": size = 1.5f; smoke = 1.4f; break;
                case "rook": size = 0.9f; break;
                case "bulldog": size = 1.6f; smoke = 1.3f; break;
                case "warden": size = 2.1f; smoke = 1.8f; break;
                case "hammerhead": size = 1.6f; smoke = 3.2f; break;
                default: size = def.Mode == FireMode.Pump ? 1.5f : def.Damage > 60f ? 1.3f : 1f; break;
            }
            CombatFX.Instance?.MuzzleFlash(muzzle, size, smoke);
        }

        // Enemies the last piercing shot went through (for tests).
        public int LastPierced { get; private set; }

        // Passes through up to def.Pierce enemies (or explosives), each hit once, each hit weaker; solid world ends the shot.
        void ShootPiercing(WeaponDefinition def, Vector3 origin, Vector3 dir, float along, Vector3 muzzlePos,
            ref bool anyHit, ref bool anyKill, ref bool anyHead)
        {
            float maxD = def.Range + along;
            int n = Physics.RaycastNonAlloc(origin, dir, s_Hits, maxD, GameLayers.ShotMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(s_Hits, 0, n, s_ByDistance);
            Vector3 end = origin + dir * def.Range;
            if (n > 0)
            {
                // Something between the muzzle and the first thing hit (a corner beside the player) takes the shot instead.
                Vector3 first = s_Hits[0].point;
                Vector3 to = first - muzzlePos;
                float dm = to.magnitude;
                if (dm > 0.3f && Physics.Raycast(muzzlePos, to / dm, out RaycastHit near, dm - 0.05f, GameLayers.ShotMask,
                        QueryTriggerInteraction.Ignore) && near.collider.GetComponentInParent<IDamageable>() == null)
                {
                    ShootRay(def, origin, dir, along, muzzlePos, ref anyHit, ref anyKill, ref anyHead);
                    return;
                }
            }

            s_Seen.Clear();
            int pierced = 0;
            float scale = 1f;
            for (int i = 0; i < n; i++)
            {
                var hit = s_Hits[i];
                var col = hit.collider;
                var hitbox = col.GetComponent<Hitbox>();
                if (hitbox == null) hitbox = col.GetComponentInParent<Hitbox>();
                IDamageable target = hitbox != null ? hitbox.Owner : col.GetComponentInParent<IDamageable>();
                HitZone zone = hitbox != null ? hitbox.Zone : HitZone.Body;
                ImpactKind kind = hitbox != null || col.gameObject.layer == GameLayers.Enemy ? ImpactKind.Enemy
                    : col.gameObject.layer == GameLayers.Explosive ? ImpactKind.Metal : ImpactKind.World;

                if (target == null)
                {
                    end = hit.point;
                    CombatFX.Instance?.Impact(hit.point, hit.normal, kind);
                    break;
                }
                if (!target.IsAlive || !s_Seen.Add(target)) continue;

                CombatFX.Instance?.Impact(hit.point, hit.normal, kind);
                float dmg = def.Damage * scale;
                bool head = zone == HitZone.Head;
                if (head) dmg *= def.HeadshotMultiplier;
                bool wasAlive = target.IsAlive;
                target.TakeDamage(new DamageInfo
                {
                    Amount = dmg,
                    Point = hit.point,
                    Direction = dir,
                    Zone = zone,
                    Kind = DamageKind.Bullet,
                    Source = gameObject,
                    FromPlayer = true,
                });
                anyHit = true;
                if (head) anyHead = true;
                if (wasAlive && !target.IsAlive) anyKill = true;
                pierced++;
                scale *= def.PierceFalloff;
                if (pierced >= def.Pierce) { end = hit.point; break; }
            }
            LastPierced = pierced;
            CombatFX.Instance?.Tracer(muzzlePos, end);
        }

        void ShootRay(WeaponDefinition def, Vector3 origin, Vector3 dir, float along, Vector3 muzzlePos,
            ref bool anyHit, ref bool anyKill, ref bool anyHead)
        {
            bool hitSomething = Physics.Raycast(origin, dir, out RaycastHit hit, def.Range + along,
                GameLayers.ShotMask, QueryTriggerInteraction.Ignore);
            Vector3 point = hitSomething ? hit.point : origin + dir * def.Range;

            // Something between the muzzle and the aim point (a corner, a car beside the player) takes the hit.
            Vector3 toPoint = point - muzzlePos;
            float dm = toPoint.magnitude;
            if (dm > 0.3f && Physics.Raycast(muzzlePos, toPoint / dm, out RaycastHit near, dm - 0.05f,
                    GameLayers.ShotMask, QueryTriggerInteraction.Ignore))
            {
                hit = near;
                point = near.point;
                hitSomething = true;
            }

            CombatFX.Instance?.Tracer(muzzlePos, point);
            if (!hitSomething) return;

            var col = hit.collider;
            var hitbox = col.GetComponent<Hitbox>();
            if (hitbox == null) hitbox = col.GetComponentInParent<Hitbox>();
            IDamageable target;
            HitZone zone = HitZone.Body;
            if (hitbox != null) { target = hitbox.Owner; zone = hitbox.Zone; }
            else target = col.GetComponentInParent<IDamageable>();

            ImpactKind kind = hitbox != null || col.gameObject.layer == GameLayers.Enemy ? ImpactKind.Enemy
                : col.gameObject.layer == GameLayers.Explosive ? ImpactKind.Metal : ImpactKind.World;
            CombatFX.Instance?.Impact(point, hit.normal, kind);

            if (target == null || !target.IsAlive) return;

            float dmg = def.Damage;
            if (def.Pellets > 1)
            {
                // Shotgun pellets lose punch with range.
                float dist = (point - muzzlePos).magnitude;
                dmg *= Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(def.Range * 0.3f, def.Range, dist));
            }
            bool head = zone == HitZone.Head;
            if (head) dmg *= def.HeadshotMultiplier;

            var info = new DamageInfo
            {
                Amount = dmg,
                Point = point,
                Direction = dir,
                Zone = zone,
                Kind = DamageKind.Bullet,
                Source = gameObject,
                FromPlayer = true,
            };
            bool wasAlive = target.IsAlive;
            target.TakeDamage(info);
            anyHit = true;
            if (head) anyHead = true;
            if (wasAlive && !target.IsAlive) anyKill = true;
        }

        static Vector3 ConeDirection(Vector3 forward, Vector3 up, float degrees)
        {
            if (degrees <= 0.001f) return forward;
            float r = degrees * Mathf.Sqrt(UnityEngine.Random.value);
            Vector3 axis = Quaternion.AngleAxis(UnityEngine.Random.value * 360f, forward) * up;
            return Quaternion.AngleAxis(r, axis) * forward;
        }
    }
}
