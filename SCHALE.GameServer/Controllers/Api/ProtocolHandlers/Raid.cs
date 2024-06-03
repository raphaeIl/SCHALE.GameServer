using SCHALE.Common.Database;
using SCHALE.Common.FlatData;
using SCHALE.Common.NetworkProtocol;
using SCHALE.GameServer.Managers;
using SCHALE.GameServer.Services;
using Serilog;

namespace SCHALE.GameServer.Controllers.Api.ProtocolHandlers
{
    public class Raid : ProtocolHandlerBase
    {
        private readonly ISessionKeyService sessionKeyService;
        private readonly SCHALEContext context;
        private readonly ExcelTableService excelTableService;

        public Raid(IProtocolHandlerFactory protocolHandlerFactory, ISessionKeyService _sessionKeyService, SCHALEContext _context, ExcelTableService _excelTableService) : base(protocolHandlerFactory)
        {
            sessionKeyService = _sessionKeyService;
            context = _context;
            excelTableService = _excelTableService;
        }

        [ProtocolHandler(Protocol.Raid_Lobby)]
        public ResponsePacket LobbyHandler(RaidLobbyRequest req)
        {
            var account = sessionKeyService.GetAccount(req.SessionKey);

            var raidSeasonExcel = excelTableService.GetTable<RaidSeasonManageExcelTable>().UnPack().DataList;
            var targetSeason = raidSeasonExcel.FirstOrDefault(x => x.SeasonId == account.RaidInfo.SeasonId);

            return new RaidLobbyResponse()
            {
                SeasonType = RaidSeasonType.Open,
                RaidLobbyInfoDB = RaidManager.Instance.GetLobby(account.RaidInfo, targetSeason),
            };
        }

        [ProtocolHandler(Protocol.Raid_CreateBattle)] // only called when fresh new raid created
        public ResponsePacket CreateBattleHandler(RaidCreateBattleRequest req)
        {
            var account = sessionKeyService.GetAccount(req.SessionKey);
            
            var raidStageExcel = excelTableService.GetTable<RaidStageExcelTable>().UnPack().DataList;
            var bossStatsExcel = excelTableService.GetTable<CharacterStatExcelTable>().UnPack().DataList;

            var currentRaidExcel = raidStageExcel.FirstOrDefault(x => x.Id == req.RaidUniqueId);
            var currentBossExcel = bossStatsExcel.FirstOrDefault(x => x.CharacterId == currentRaidExcel.RaidCharacterId);

            account.RaidInfo.CurrentRaidUniqueId = req.RaidUniqueId;
            account.RaidInfo.CurrentDifficulty = req.Difficulty;

            context.Entry(account).Property(x => x.RaidInfo).IsModified = true; // force update
            context.SaveChanges();
            
            var raid = RaidManager.Instance.CreateRaid(account, currentBossExcel, req.IsPractice);
            var battle = RaidManager.Instance.CreateBattle(account);

            return new RaidCreateBattleResponse()
            {
                RaidDB = raid,
                RaidBattleDB = battle,
                AssistCharacterDB = new () { }
            };
        }

        [ProtocolHandler(Protocol.Raid_EnterBattle)] // clicked restart
        public ResponsePacket EnterBattleHandler(RaidEnterBattleRequest req)
        {
            var account = sessionKeyService.GetAccount(req.SessionKey);

            var raid = RaidManager.Instance.RaidDB;
            var battle = RaidManager.Instance.RaidBattleDB;

            var currentTeam = account.Echelons.Where(x => x.EchelonType == EchelonType.Raid && x.EchelonNumber == req.EchelonId)
                                              .FirstOrDefault();

            if (!raid.ParticipateCharacterServerIds.ContainsKey(account.ServerId))
                raid.ParticipateCharacterServerIds.Add(account.ServerId, new());

            raid.ParticipateCharacterServerIds[account.ServerId] = currentTeam.MainSlotServerIds.Concat(currentTeam.SupportSlotServerIds).ToList();

            return new RaidEnterBattleResponse() 
            {
                RaidDB = raid,
                RaidBattleDB = battle,
                AssistCharacterDB = new() { }
            };
        }

        [ProtocolHandler(Protocol.Raid_EndBattle)]
        public ResponsePacket EndBattleHandler(RaidEndBattleRequest req)
        {
            var account = sessionKeyService.GetAccount(req.SessionKey);

            var raidStageTable = excelTableService.GetTable<RaidStageExcelTable>().UnPack().DataList;
            var currentRaidData = raidStageTable.FirstOrDefault(x => x.Id == account.RaidInfo.CurrentRaidUniqueId);

            var timeScore = RaidManager.CalculateTimeScore(req.Summary.ElapsedRealtime, account.RaidInfo.CurrentDifficulty);
            var hpPercentScorePoint = currentRaidData.HPPercentScore;
            var defaultClearPoint = currentRaidData.DefaultClearScore;

            var rankingPoint = timeScore + hpPercentScorePoint + defaultClearPoint;

            account.RaidInfo.BestRankingPoint = rankingPoint > account.RaidInfo.BestRankingPoint ? rankingPoint : account.RaidInfo.BestRankingPoint;
            account.RaidInfo.TotalRankingPoint += rankingPoint;
            context.Entry(account).Property(x => x.RaidInfo).IsModified = true; // force update
            context.SaveChanges();

            bool raidDone = RaidManager.Instance.EndBattle(account, req.Summary.RaidSummary.RaidBossResults.FirstOrDefault());

            if (!raidDone)
                return new RaidEndBattleResponse();

            RaidManager.Instance.FinishRaid(account.RaidInfo);

            return new RaidEndBattleResponse()
            {
                RankingPoint = rankingPoint,
                BestRankingPoint = account.RaidInfo.BestRankingPoint,
                ClearTimePoint = timeScore,
                HPPercentScorePoint = hpPercentScorePoint,
                DefaultClearPoint = defaultClearPoint
            };
        }

        [ProtocolHandler(Protocol.Raid_GiveUp)]
        public ResponsePacket GiveUpHandler(RaidGiveUpRequest req)
        {
            var account = sessionKeyService.GetAccount(req.SessionKey);

            RaidManager.Instance.FinishRaid(account.RaidInfo);

            return new RaidGiveUpResponse()
            {
                Tier = 0,
                RaidGiveUpDB = new()
                {
                    Ranking = 0,
                    RankingPoint = 0,
                    BestRankingPoint = account.RaidInfo.BestRankingPoint
                }
            };
        }

        [ProtocolHandler(Protocol.Raid_OpponentList)]
        public ResponsePacket OpponentListHandler(RaidOpponentListRequest req)
        {
            return new RaidOpponentListResponse()
            {
                OpponentUserDBs = []
            };
        }
    }
}
