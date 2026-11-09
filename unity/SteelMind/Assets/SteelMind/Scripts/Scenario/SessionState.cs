using UnityEngine;

namespace SteelMind.Scenario
{
    /// <summary>
    /// 한 번 플레이하는 동안만 남는 기록(장면을 바꿔도 유지). 메인 화면·인트로를 한 번만 보여 주는 데 쓴다.
    /// Domain Reload가 꺼져 있어도 플레이를 멈추면 이 오브젝트가 사라지므로 static 대신 이것을 쓴다.
    /// </summary>
    public class SessionState : MonoBehaviour
    {
        public bool titleShown;
    }
}
