using System;
using System.Collections;
using System.Collections.Generic;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using UnityEngine;
using UnityEngine.InputSystem;
using LNStudy.Tools;

namespace LNStudy.Player
{
    /// <summary>
    /// 두 캐릭터 파티. 둘 다 항상 필드에 있고, Tab으로 플레이어가 조종할 캐릭터를 고른다.
    /// 조종하지 않는 캐릭터는 CatFollower로 따라오거나(따라와) 그 자리에서 기다린다(기다려).
    /// - leftBehindWaits: 교체할 때 두고 온 캐릭터는 그 자리에서 기다린다 (발판 누르고 있기 같은 협동 퍼즐용).
    ///   F(패드 Y)로 동료를 부르거나 다시 기다리게 한다.
    /// - 카메라: ThirdPersonCamera 또는 SideFollowCamera(2.5D) 대상 교체
    /// - 피격: 넉백 + 피격 모션 / 쓰러짐: 잠시 뒤 부활
    /// - 낙사(killY 아래): 체크포인트가 있으면 그곳, 없으면 동료 옆으로
    /// </summary>
    public class CatParty : MonoBehaviour
    {
        public static CatParty Instance { get; private set; }

        [Serializable]
        public class Member
        {
            public string displayName = "Cat";
            public PlayerInputReader input;
            public CatFollower follower;
            public Health health;
            [Tooltip("모델(Animator) 루트. 쓰러지면 숨긴다")]
            public GameObject visual;
        }

        [SerializeField] Member[] members = new Member[0];
        [SerializeField] ThirdPersonCamera cam;
        [SerializeField] SideFollowCamera sideCam;
        [SerializeField] int startIndex;
        [Tooltip("교체할 때 두고 온 캐릭터는 따라오지 않고 기다린다 (F로 부름)")]
        [SerializeField] bool leftBehindWaits;
        [SerializeField] float reviveDelay = 2.5f;
        [SerializeField] float killY = -40f;
        [SerializeField] Transform fallbackSpawn;
        [SerializeField] float switchCooldown = 0.25f;
        [Tooltip("HUD 이름 옆에 붙일 안내 (비우면 없음)")]
        [SerializeField] bool showPartnerHint = true;

        public int ActiveIndex { get; private set; } = -1;
        public Member Active => ActiveIndex >= 0 && ActiveIndex < members.Length ? members[ActiveIndex] : null;
        public event Action<Member> Switched;
        /// <summary>연출 중: 아무도 조종하지 않고 따라오지도 않는다</summary>
        public bool Locked { get; private set; }
        public Member[] Members => members;

        InputAction switchAction, callAction;
        float nextSwitchTime;
        Vector3? checkpoint;
        readonly HashSet<Member> waiting = new();
        readonly Dictionary<Health, Action<DamageInfo>> damageHandlers = new();
        readonly Dictionary<Health, Action> deathHandlers = new();

        void Awake()
        {
            Instance = this;
            switchAction = new InputAction("SwitchCharacter", InputActionType.Button, "<Keyboard>/tab");
            switchAction.AddBinding("<Gamepad>/dpad/right");
            callAction = new InputAction("CallPartner", InputActionType.Button, "<Keyboard>/f");
            callAction.AddBinding("<Gamepad>/buttonNorth");
            if (cam == null && sideCam == null) cam = FindFirstObjectByType<ThirdPersonCamera>();
        }

        void OnEnable()
        {
            switchAction.Enable();
            callAction.Enable();
            foreach (var m in members)
            {
                if (m.health == null) continue;
                var mm = m;
                Action<DamageInfo> dh = info => OnDamaged(mm, info);
                Action de = () => OnDied(mm);
                damageHandlers[m.health] = dh;
                deathHandlers[m.health] = de;
                m.health.Damaged += dh;
                m.health.Died += de;
            }
        }

        void OnDisable()
        {
            switchAction.Disable();
            callAction.Disable();
            foreach (var kv in damageHandlers) if (kv.Key != null) kv.Key.Damaged -= kv.Value;
            foreach (var kv in deathHandlers) if (kv.Key != null) kv.Key.Died -= kv.Value;
            damageHandlers.Clear();
            deathHandlers.Clear();
        }

        void OnDestroy()
        {
            switchAction?.Dispose();
            callAction?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            Activate(Mathf.Clamp(startIndex, 0, members.Length - 1), true);
        }

        void Update()
        {
            if (members.Length == 0 || Locked) return;

            if (switchAction.WasPressedThisFrame() && Time.time >= nextSwitchTime)
            {
                int next = NextAlive(ActiveIndex);
                if (next >= 0 && next != ActiveIndex && CanSwitchFrom(Active)) Activate(next, false);
            }

            if (callAction.WasPressedThisFrame()) TogglePartnerFollow();

            // 맵 밖으로 떨어지면 복귀
            foreach (var m in members)
                if (m.input != null && m.input.transform.position.y < killY)
                    Respawn(m);

            UpdateHud();
        }

        public void SetCheckpoint(Vector3 position) => checkpoint = position;

        /// <summary>연출용 잠금. 풀면 지금 조종 중이던 캐릭터로 되돌린다</summary>
        public void SetLocked(bool locked)
        {
            Locked = locked;
            if (locked)
            {
                foreach (var m in members)
                {
                    if (m.input == null) continue;
                    var grab = m.input.GetComponent<PlayerGrab>();
                    if (grab != null && grab.Current != null) grab.Release();
                    m.input.SetControlled(false);
                    if (m.follower != null) m.follower.SetFollowing(false, null);
                }
            }
            else ApplyStates();
        }

        bool CanSwitchFrom(Member m)
        {
            if (m == null || m.input == null) return true;
            var motor = m.input.GetComponent<PlayerMotor>();
            return motor == null || !motor.IsClimbing;
        }

        int NextAlive(int from)
        {
            for (int i = 1; i <= members.Length; i++)
            {
                int idx = (from + i + members.Length) % members.Length;
                var h = members[idx].health;
                if (h == null || !h.IsDead) return idx;
            }
            return -1;
        }

        bool IsDead(Member m) => m.health != null && m.health.IsDead;

        public void Activate(int index, bool instant)
        {
            if (index < 0 || index >= members.Length) return;
            var previous = Active;
            ActiveIndex = index;
            nextSwitchTime = Time.time + switchCooldown;
            var leader = members[index];

            // 두고 온 캐릭터는 그 자리에서 기다린다
            waiting.Remove(leader);
            if (leftBehindWaits && previous != null && previous != leader) waiting.Add(previous);

            ApplyStates();

            if (cam != null)
            {
                if (instant) cam.Target = leader.input.transform;
                else cam.SetTargetSmooth(leader.input.transform);
            }
            if (sideCam != null) sideCam.SetTarget(leader.input.transform);

            if (StageHUD.Instance != null)
            {
                StageHUD.Instance.SetPlayerHealth(leader.health);
                if (!instant) StageHUD.Instance.ShowToast($"{leader.displayName}", 1f);
            }
            Switched?.Invoke(leader);
        }

        /// <summary>조종 중인 캐릭터 기준으로 나머지의 입력·추종 상태를 다시 맞춘다</summary>
        void ApplyStates()
        {
            var leader = Active;
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m.input == null) continue;
                bool active = m == leader;
                bool dead = IsDead(m);

                if (!active)
                {
                    var grab = m.input.GetComponent<PlayerGrab>();
                    if (grab != null && grab.Current != null) grab.Release();
                }

                m.input.SetControlled(active && !dead);
                bool follow = !active && !dead && !waiting.Contains(m);
                if (m.follower != null) m.follower.SetFollowing(follow, leader.input.transform);
                if (active)
                {
                    var motor = m.input.GetComponent<PlayerMotor>();
                    if (motor != null) motor.UseExternalMove = false;
                }
            }
        }

        /// <summary>F: 동료 "따라와" ↔ "기다려"</summary>
        void TogglePartnerFollow()
        {
            bool anyWaiting = false;
            foreach (var m in members) if (m != Active && waiting.Contains(m)) anyWaiting = true;
            foreach (var m in members)
            {
                if (m == Active) continue;
                if (anyWaiting) waiting.Remove(m); else waiting.Add(m);
            }
            ApplyStates();
            StageHUD.Instance?.ShowToast(anyWaiting ? "따라와!" : "기다려!", 1f);
            if (Active != null && Active.input != null)
                Sfx.Play("call", Active.input.transform.position, 0.5f, anyWaiting ? 1.1f : 0.85f);
            foreach (var m in members)
            {
                if (m == Active || m.input == null) continue;
                var juice = m.input.GetComponentInChildren<CharacterJuice>();
                if (juice != null) juice.Pop(anyWaiting ? 0.3f : -0.2f);
            }
        }

        void UpdateHud()
        {
            var hud = StageHUD.Instance;
            var a = Active;
            if (hud == null || a == null || a.input == null) return;
            string label = a.displayName;
            var thrower = a.input.GetComponentInChildren<RamenThrower>();
            if (thrower != null && thrower.isActiveAndEnabled)
                label += thrower.CooldownRemaining > 0f ? $"   컵케이크 {thrower.CooldownRemaining:F1}s" : "   컵케이크 준비";
            var grab = a.input.GetComponent<PlayerGrab>();
            if (grab != null && grab.Current != null) label += "   [상자 잡는 중]";
            if (showPartnerHint && members.Length > 1)
            {
                bool partnerWaits = false;
                foreach (var m in members) if (m != a && waiting.Contains(m)) partnerWaits = true;
                label += partnerWaits ? "   · 동료: 기다리는 중 (F 부르기)" : "   · 동료: 따라오는 중 (F 기다려)";
            }
            hud.ExternalLabel = label;
        }

        void OnDamaged(Member m, DamageInfo info)
        {
            if (m.input == null) return;
            var vis = m.input.GetComponentInChildren<ICharacterVisual>();
            vis?.PlayHit();
            var motor = m.input.GetComponent<PlayerMotor>();
            if (motor != null && info.knockback > 0f)
            {
                Vector3 push = info.direction;
                push.y = 0f;
                if (push.sqrMagnitude > 0.001f) motor.AddImpulse(push.normalized * info.knockback);
            }
            if (m == Active) ThirdPersonCamera.Active?.Shake(0.06f, 0.15f);
        }

        void OnDied(Member m) => StartCoroutine(DeathRoutine(m));

        IEnumerator DeathRoutine(Member m)
        {
            var body = m.input.GetComponent<Rigidbody>();
            var grab = m.input.GetComponent<PlayerGrab>();
            if (grab != null && grab.Current != null) grab.Release();

            m.input.SetControlled(false);
            if (m.follower != null) m.follower.SetFollowing(false, null);
            if (m.visual != null) m.visual.SetActive(false);
            if (body != null) { body.linearVelocity = Vector3.zero; body.isKinematic = true; }
            foreach (var c in m.input.GetComponents<Collider>()) c.enabled = false;

            StageHUD.Instance?.ShowToast($"{m.displayName}이(가) 쓰러졌다! 잠시 후 돌아온다", 2f);

            if (m == Active)
            {
                int next = NextAlive(ActiveIndex);
                if (next >= 0 && members[next] != m) Activate(next, false);
            }

            yield return new WaitForSeconds(reviveDelay);

            foreach (var c in m.input.GetComponents<Collider>()) c.enabled = true;
            if (body != null) body.isKinematic = false;
            Respawn(m);
            if (m.visual != null) m.visual.SetActive(true);
            m.health.Revive();

            int idx = Array.IndexOf(members, m);
            var active = Active;
            if (active == null || IsDead(active)) Activate(idx, false);
            else ApplyStates();
        }

        /// <summary>체크포인트(있으면) 또는 동료 옆으로 되돌린다</summary>
        public void Respawn(Member m)
        {
            var body = m.input.GetComponent<Rigidbody>();
            var grab = m.input.GetComponent<PlayerGrab>();
            if (grab != null && grab.Current != null) grab.Release();

            Vector3 dest;
            if (checkpoint.HasValue)
            {
                int idx = Array.IndexOf(members, m);
                dest = checkpoint.Value + Vector3.left * (idx * 0.9f) + Vector3.up * 0.2f;
            }
            else
            {
                Member partner = null;
                foreach (var o in members)
                    if (o != m && o.input != null && !IsDead(o) && o.input.transform.position.y > killY) { partner = o; break; }
                if (partner != null && m.follower != null)
                {
                    m.follower.SetFollowing(true, partner.input.transform);
                    m.follower.TeleportToLeader();
                    ApplyStates();
                    return;
                }
                dest = fallbackSpawn != null ? fallbackSpawn.position : m.input.transform.position + Vector3.up * 50f;
            }

            if (body != null) { body.position = dest; body.linearVelocity = Vector3.zero; }
            m.input.transform.position = dest;
            ApplyStates();
        }
    }
}
