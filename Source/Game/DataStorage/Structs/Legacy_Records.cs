namespace Game.DataStorage
{
    // AvailableSuperDistrict 87BD1F4D and SuperDistrictSetXAvailableSD 19A3AA0B.
    public sealed class AvailableSuperDistrictRecord
    {
        public LocalizedString DisplayName;
        public LocalizedString Description;
        public string OverrideBgAtlas;
        public string OverrideBgGlowAtlas;
        public int Id;
        public int SuperDistrictID;
        public int IconFileDataID;
        public int OrderIndex;
    }
    public sealed class SuperDistrictSetXAvailableSDRecord
    {
        public int Id;
        public int AvailableSuperDistrictID;
        public int SuperDistrictSetID;
    }

    // RenownRewards layout 30AFBCF8, including Forever 1.60.1.70205 and 70425.
    // https://github.com/wowdev/WoWDBDefs/blob/master/definitions/RenownRewards.dbd
    public sealed class RenownRewardsRecord
    {
        public int Id;
        public LocalizedString Name;
        public LocalizedString Description;
        public LocalizedString ToastDescription;
        public int CovenantID;
        public int Level;
        public int Icon;
        public int Flags;
        public int UiOrder;
        public int ItemID;
        public int SpellID;
        public int MountID;
        public int TransmogID;
        public int TransmogSetID;
        public int CharTitlesID;
        public int GarrFollowerID;
        public int TransmogIllusionID;
        public int Unknown1200;
        public int QuestID;
        public int PlayerConditionID;
    }
}
