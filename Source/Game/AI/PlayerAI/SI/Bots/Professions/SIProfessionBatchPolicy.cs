using Framework.Constants;
using System;

namespace Game.AI;

public static class SIProfessionBatchPolicy
{
    public const int TargetPoints=20;
    public static int SkillTarget(int rank,int cap,int grey)=>Math.Max(rank,Math.Min(rank+TargetPoints,Math.Min(cap,grey)));
    public static int Crafts(int rank,int cap,int yellow,int grey,int skillUps)
    {
        int points=SkillTarget(rank,cap,grey)-rank;
        if(points<=0)return 0;
        // Yellow/green recipes are not guaranteed gains. Budget extra attempts,
        // then stop the retained batch at its actual skill target or grey rank.
        double attempts=0;
        for(int value=rank;value<rank+points;value+=Math.Max(1,skillUps))
            attempts+=1/(value<yellow?1:value<(yellow+grey)/2?.67:.34);
        return Math.Clamp((int)Math.Ceiling(attempts),1,80);
    }
    public static int Reagents(int perCraft,int crafts)=>(int)Math.Clamp((long)perCraft*crafts,0,int.MaxValue);
    public static bool NaturalMaterial(ItemClass itemClass,int subclass)=>itemClass==ItemClass.TradeGoods&&
        (ItemSubClassTradeGoods)subclass is ItemSubClassTradeGoods.Cloth or ItemSubClassTradeGoods.Leather or
        ItemSubClassTradeGoods.MetalStone or ItemSubClassTradeGoods.Meat or ItemSubClassTradeGoods.Herb or
        ItemSubClassTradeGoods.Elemental or ItemSubClassTradeGoods.Enchanting;
    public static bool VendorAllowed(int item)
    {
        var template=Global.ObjectMgr.GetItemTemplate(item);
        if(template!=null&&NaturalMaterial(template.GetClass(),template.GetSubClass().data))return false;
        return !SIProfessionKnowledge.IsBuilt||SIProfessionKnowledge.Instance.NodesGiving(item).Length==0&&
            SIProfessionKnowledge.Instance.SkinnersGiving(item).Length==0;
    }
}
