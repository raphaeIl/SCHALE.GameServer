using Castle.Core.Logging;
using SCHALE.Common.Database;
using SCHALE.Common.FlatData;
using SCHALE.Common.NetworkProtocol;
using SCHALE.GameServer.Controllers.Api.ProtocolHandlers;
using SCHALE.GameServer.Utils;

namespace SCHALE.GameServer.Managers
{
    public class RaidManager : Singleton<RaidManager>
    {
        public SingleRaidLobbyInfoDB RaidLobbyInfoDB { get; private set; }

        public RaidDB RaidDB {  get; private set; }

        public RaidBattleDB RaidBattleDB { get; private set; }

        public SingleRaidLobbyInfoDB GetLobby(RaidInfo raidInfo, RaidSeasonManageExcelT targetSeasonData)
        {
            if (RaidLobbyInfoDB == null || RaidLobbyInfoDB.SeasonId != raidInfo.SeasonId)
            {
                RaidLobbyInfoDB = new SingleRaidLobbyInfoDB()
                {
                    SeasonId = raidInfo.SeasonId,
                    Tier = 0,
                    Ranking = 1,
                    BestRankingPoint = raidInfo.BestRankingPoint,
                    TotalRankingPoint = raidInfo.TotalRankingPoint,
                    ReceiveRewardIds = [],
                    PlayableHighestDifficulty = new()
                    {
                        { targetSeasonData.OpenRaidBossGroup.FirstOrDefault(), Difficulty.Torment }
                    },

                    PlayingRaidDB = RaidDB,
                };
            }
            
            else
            {
                RaidLobbyInfoDB.BestRankingPoint = raidInfo.BestRankingPoint;
                RaidLobbyInfoDB.TotalRankingPoint = raidInfo.TotalRankingPoint;
                RaidLobbyInfoDB.PlayingRaidDB = RaidDB;
            }

            return RaidLobbyInfoDB;
        }

        public RaidDB CreateRaid(AccountDB account, CharacterStatExcelT bossData, bool isPractice)
        {
            if (RaidDB == null)
            {
                RaidDB = new()
                {
                    Owner = new()
                    {
                        AccountId = account.ServerId,
                        AccountName = account.Nickname,
                    },

                    ContentType = ContentType.Raid,
                    ServerId = 1,
                    UniqueId = account.RaidInfo.CurrentRaidUniqueId,
                    SeasonId = account.RaidInfo.SeasonId,
                    Begin = DateTime.UtcNow,
                    End = DateTime.UtcNow.AddDays(2), // idk jp server time or whatever but if this is is wrong it breaks
                    PlayerCount = 1,
                    SecretCode = "0",
                    RaidState = RaidStatus.Playing,
                    IsPractice = isPractice,
                    RaidBossDBs = [
                        new() {
                            ContentType = ContentType.Raid,
                            BossCurrentHP = bossData.MaxHP100,
                            BossGroggyPoint = bossData.GroggyGauge
                        }
                    ],

                    AccountLevelWhenCreateDB = account.Level
                };
            }

            else
            {
                RaidDB.BossDifficulty = account.RaidInfo.CurrentDifficulty;
                RaidDB.UniqueId = account.RaidInfo.CurrentRaidUniqueId;
                RaidDB.IsPractice = isPractice;
            }

            return RaidDB;
        }

        public RaidBattleDB CreateBattle(AccountDB account)
        {
            if (RaidBattleDB == null)
            {
                RaidBattleDB = new()
                {
                    ContentType = ContentType.Raid,
                    RaidUniqueId = account.RaidInfo.CurrentRaidUniqueId,
                    CurrentBossHP = this.RaidDB.RaidBossDBs.FirstOrDefault().BossCurrentHP,
                    CurrentBossGroggy = this.RaidDB.RaidBossDBs.FirstOrDefault().BossGroggyPoint,
                    RaidMembers = [
                        new() {
                            AccountId = account.ServerId,
                            AccountName = account.Nickname,
                        }
                    ]
                };
            }

            else
            {
                RaidBattleDB.RaidUniqueId = account.RaidInfo.CurrentRaidUniqueId;
            }

            return RaidBattleDB;
        }

        public bool EndBattle(AccountDB account, RaidBossResult bossResult)
        {
            var battle = Instance.RaidBattleDB;
            var raid = Instance.RaidDB;

            battle.CurrentBossHP -= bossResult.RaidDamage.GivenDamage;
            battle.CurrentBossGroggy -= bossResult.RaidDamage.GivenGroggyPoint;
            battle.CurrentBossAIPhase = bossResult.AIPhase;
            battle.SubPartsHPs = bossResult.SubPartsHPs;

            raid.RaidBossDBs.FirstOrDefault().BossCurrentHP = battle.CurrentBossHP;
            raid.RaidBossDBs.FirstOrDefault().BossGroggyPoint = battle.CurrentBossGroggy;

            RaidMemberDescription raidMember = RaidBattleDB.RaidMembers[account.ServerId]; // no checks, guaranteed exists since it is add in Raid_CreateBattle

            raidMember.DamageCollection.Add(new()
            {
                Index = raidMember.DamageCollection.Count,
                GivenDamage = bossResult.RaidDamage.GivenDamage,
                GivenGroggyPoint = bossResult.RaidDamage.GivenGroggyPoint
            });

            return battle.CurrentBossHP <= 0;
        }

        public void FinishRaid(RaidInfo raidInfo)
        {
            RaidLobbyInfoDB.BestRankingPoint = raidInfo.BestRankingPoint;
            RaidLobbyInfoDB.TotalRankingPoint = raidInfo.TotalRankingPoint;
            RaidLobbyInfoDB.PlayingRaidDB = null;

            RaidDB = null;
            RaidBattleDB = null;
        }

        public static long CalculateTimeScore(float duration, Difficulty difficulty)
        {
            int[] multipliers = [120, 240, 480, 960, 1440, 1920, 2400]; // from wiki

            return (long)((3600f - duration) * multipliers[(int)difficulty]);
        }
    }
}
