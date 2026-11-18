using System.Collections;
using System.Collections.Generic;
using LNStudy.CameraSystem;
using LNStudy.Combat;
using LNStudy.Gimmicks;
using LNStudy.Player;
using LNStudy.Tools;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LNStudy.AI
{
    /// <summary>
    /// 학교 방어 진행: 웨이브마다 복도 양 끝 문에서 양 몬스터가 들어오고, 친구들을 지켜야 한다.
    /// - 시작 조건(퀘스트): 친구 모두에게 말을 걸어야 양 떼가 쳐들어온다 (순서 상관 없음)
    /// - 모든 웨이브를 막으면 승리 (승리 포즈) / 친구가 한 명이라도 쓰러지면 패배
    /// - 끝나면 R로 다시 시작
    /// HUD는 StageHUD(배너·알림·목표·체력바)를 쓴다.
    /// </summary>
    public class SchoolDefenseGame : MonoBehaviour
    {
        public static SchoolDefenseGame Instance { get; private set; }

        [System.Serializable]
        public class Wave
        {
            public int count = 4;
            public int bigCount;
            public float interval = 1.2f;
        }

        [SerializeField] GameObject lambPrefab;
        [SerializeField] GameObject bigLambPrefab;
        [SerializeField] Transform[] spawnPoints = new Transform[0];
        [SerializeField] Wave[] waves = new Wave[0];
        [SerializeField] FriendNPC[] friends = new FriendNPC[0];
        [SerializeField] SwordCombat player;
        [SerializeField] float firstDelay = 4f;
        [SerializeField] float betweenWaves = 4f;
        [SerializeField] string victoryState = "Victory";

        public int WaveIndex { get; private set; } = -1;
        public bool Finished { get; private set; }

        readonly List<Health> alive = new();
        int spawning;

        void Awake()
        {
            Instance = this;
            // 바리케이드 carving으로 길이 끊기면 경로 계산(닿을 수 없음 확인)이 길어진다 → 프레임당 탐색량을 늘린다
            NavMesh.pathfindingIterationsPerFrame = 500;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start() => StartCoroutine(Run());

        int AliveCount()
        {
            alive.RemoveAll(h => h == null || h.IsDead);
            return alive.Count;
        }

        void Update()
        {
            var hud = StageHUD.Instance;
            if (hud != null && !Finished)
            {
                string friendsLine = "";
                foreach (var f in friends)
                    if (f != null) friendsLine += $"{f.DisplayName} {Mathf.CeilToInt(f.Health.Current)}  ";
                int talked = 0;
                foreach (var f in friends) if (f != null && f.Talked) talked++;
                hud.ObjectiveOverride = !QuestDone
                    ? $"<b>친구들과 이야기하기</b>  {talked} / {friends.Length}\nF: 가까이 가서 말 걸기"
                    : WaveIndex < 0
                    ? $"<b>학교를 지켜라</b>\n양 떼가 몰려온다!\n{friendsLine}"
                    : $"<b>웨이브 {WaveIndex + 1} / {waves.Length}</b>\n남은 양 {AliveCount() + spawning}\n{friendsLine}";
            }

            if (Finished)
            {
                bool restart = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                               || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
                if (restart) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                    ? SceneManager.GetActiveScene().buildIndex : 0);
            }
        }

        public bool QuestDone { get; private set; }

        bool AllTalked()
        {
            foreach (var f in friends) if (f != null && !f.Talked) return false;
            return true;
        }

        /// <summary>친구와의 대화가 끝날 때마다 (DoorInteractor가 부름)</summary>
        public void OnTalked(FriendNPC friend)
        {
            if (QuestDone || !AllTalked()) return;
            QuestDone = true;
        }

        IEnumerator Run()
        {
            // 퀘스트: 모두와 대화할 때까지 대기
            while (!QuestDone) yield return null;
            Sfx.Play("rumble", 1f, 0.7f);
            ThirdPersonCamera.Active?.Shake(0.12f, 1.2f);
            StageHUD.Instance?.ShowBanner("양 떼가 쳐들어온다!", 2.5f);
            yield return new WaitForSeconds(firstDelay);
            for (int w = 0; w < waves.Length && !Finished; w++)
            {
                WaveIndex = w;
                var wave = waves[w];
                bool last = w == waves.Length - 1;
                StageHUD.Instance?.ShowBanner(last ? "마지막 웨이브!" : $"웨이브 {w + 1}", 2f);
                Sfx.Play("rumble", 0.6f, 1.2f);

                int total = wave.count + wave.bigCount;
                spawning = total;
                for (int i = 0; i < total && !Finished; i++)
                {
                    bool big = i >= wave.count;
                    Spawn(big ? bigLambPrefab : lambPrefab, i);
                    spawning--;
                    yield return new WaitForSeconds(wave.interval);
                }
                spawning = 0;

                while (!Finished && AliveCount() > 0) yield return null;
                if (Finished) yield break;

                if (!last)
                {
                    StageHUD.Instance?.ShowToast("잘했어! 다음 무리가 온다", 2f);
                    yield return new WaitForSeconds(betweenWaves);
                }
            }
            if (!Finished) Win();
        }

        void Spawn(GameObject prefab, int index)
        {
            if (prefab == null || spawnPoints.Length == 0) return;
            var sp = spawnPoints[index % spawnPoints.Length];
            Vector3 pos = sp.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.5f, 0.5f));
            var pa = prefab.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = pa != null ? pa.agentTypeID : 0, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(pos, out var hit, 2f, filter)) pos = hit.position;
            var go = Instantiate(prefab, pos, sp.rotation);
            var h = go.GetComponent<Health>();
            if (h != null) alive.Add(h);
        }

        public void OnFriendDown(FriendNPC friend)
        {
            if (Finished) return;
            Finished = true;
            StageHUD.Instance?.ShowBanner($"{friend.DisplayName}이(가) 쓰러졌다…", 999f);
            if (StageHUD.Instance != null) StageHUD.Instance.ObjectiveOverride = "R 키: 다시 하기";
            if (player != null) player.Locked = true;
            Sfx.Play("thud", 0.9f, 0.6f);
            StopMonsters();
        }

        void Win()
        {
            Finished = true;
            StageHUD.Instance?.ShowBanner("친구들을 지켜냈다!", 999f);
            if (StageHUD.Instance != null) StageHUD.Instance.ObjectiveOverride = "R 키: 다시 하기";
            Sfx.Play("chime", 0.9f, 1f);
            if (player != null) { player.Locked = true; player.PlayState(victoryState); }
        }

        void StopMonsters()
        {
            foreach (var m in FindObjectsByType<MonsterBrain>(FindObjectsSortMode.None)) m.enabled = false;
            foreach (var a in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None)) if (a.isOnNavMesh) a.isStopped = true;
        }
    }
}
