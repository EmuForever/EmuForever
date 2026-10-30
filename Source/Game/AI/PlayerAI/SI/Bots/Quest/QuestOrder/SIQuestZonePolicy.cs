namespace Game.AI;

public static class SIQuestZonePolicy
{
    public static bool Allows(int current,int destination,int origin,bool accepted,bool authorized,bool turnIn)=>
        current<=0||destination<=0||current==destination||accepted&&(origin==current||authorized||turnIn&&destination==origin);
}
