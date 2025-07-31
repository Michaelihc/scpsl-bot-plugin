namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal interface IAction
    {
        void SetEnabledByBeliefs(FpcMind fpcMind);
        void SetImpactsBeliefs(FpcMind fpcMind);

        //float Score { get; }
        float Cost { get; }
        float HeuristicCost => 0f;

        void Tick(FpcMatchProvider matchProvider);
        void Reset();
    }
}
