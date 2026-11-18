using LNStudy.AI;
using LNStudy.Gimmicks;
using LNStudy.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LNStudy.Player
{
    /// <summary>
    /// F(패드 B) 상호작용. 가까운 친구가 있으면 대화(우선), 아니면 가까운 문을 열고 닫는다.
    /// 처음 범위에 들어가면 안내를 한 번 띄운다. 잡을 수 있는 사물함 근처면 E(잡기) 안내도 띄운다.
    /// </summary>
    public class DoorInteractor : MonoBehaviour
    {
        [SerializeField] float reach = 1.6f;
        [SerializeField] float talkReach = 1.9f;

        InputAction use;
        SwingDoor[] doors;
        FriendNPC[] friends;
        PlayerGrab grab;
        Object lastHinted;

        void Awake()
        {
            use = new InputAction("Interact", InputActionType.Button, "<Keyboard>/f");
            use.AddBinding("<Gamepad>/buttonEast");
        }

        void Start()
        {
            doors = FindObjectsByType<SwingDoor>(FindObjectsSortMode.None);
            friends = FindObjectsByType<FriendNPC>(FindObjectsSortMode.None);
            grab = GetComponent<PlayerGrab>();
        }

        Grabbable NearestGrabbable()
        {
            if (grab == null || grab.Current != null) return null;
            Grabbable best = null;
            float bestD = 1.3f;
            foreach (var g in FindObjectsByType<Grabbable>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled) continue;
                Vector3 to = g.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude < bestD) { bestD = to.magnitude; best = g; }
            }
            return best;
        }

        float nextGrabScan;
        Grabbable nearGrab;

        void OnEnable() => use.Enable();
        void OnDisable() => use.Disable();
        void OnDestroy() => use.Dispose();

        SwingDoor NearestDoor()
        {
            SwingDoor best = null;
            float bestD = reach;
            if (doors == null) return null;
            foreach (var d in doors)
            {
                if (d == null) continue;
                float dist = d.DistanceTo(transform.position + Vector3.up * 0.6f);
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        FriendNPC NearestFriend()
        {
            FriendNPC best = null;
            float bestD = talkReach;
            if (friends == null) return null;
            foreach (var f in friends)
            {
                if (f == null || f.Health.IsDead || f.Panicking || f.TalkLines == null || f.TalkLines.Length == 0) continue;
                Vector3 to = f.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude < bestD) { bestD = to.magnitude; best = f; }
            }
            return best;
        }

        void Update()
        {
            var dialogue = DialogueDirector.Instance;
            if (dialogue.Busy) return;

            var friend = NearestFriend();
            var door = friend == null ? NearestDoor() : null;
            if (Time.time >= nextGrabScan) { nextGrabScan = Time.time + 0.25f; nearGrab = NearestGrabbable(); }
            Object target = friend != null ? friend : door != null ? door : nearGrab;

            if (target != null && target != lastHinted)
            {
                lastHinted = target;
                string hint = friend != null ? $"F: {friend.DisplayName}에게 말 걸기"
                            : door != null ? (door.Jammed ? "사물함에 막혀 있다" : door.IsOpen ? "F: 문 닫기" : "F: 문 열기")
                            : "E: 사물함 잡기 (다시 E: 놓기)";
                StageHUD.Instance?.ShowToast(hint, 1.2f);
            }
            if (target == null) lastHinted = null;

            if (!use.WasPressedThisFrame()) return;
            if (friend != null)
            {
                var f = friend;
                dialogue.Play(f, transform, f.TalkLines, () =>
                {
                    lastHinted = null;
                    SchoolDefenseGame.Instance?.OnTalked(f);
                });
            }
            else if (door != null)
            {
                if (door.Jammed) StageHUD.Instance?.ShowToast("사물함이 막고 있어서 움직이지 않는다", 1.2f);
                door.Toggle(transform.position);
            }
        }
    }
}
