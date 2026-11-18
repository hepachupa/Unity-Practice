namespace LNStudy.Player
{
    /// <summary>
    /// 캐릭터 고유 기본 공격. 외형 루트에 이 인터페이스를 구현한 컴포넌트가 있고 켜져 있으면
    /// PlayerCombat은 드론 기본 공격을 하지 않고 그 캐릭터에게 맡긴다. (예: 시사의 라멘 던지기)
    /// </summary>
    public interface IPrimaryAttackOverride
    {
        bool IsOverriding { get; }
    }
}
