using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using Auxiliary.Packets;
using static AlternativeBans.BansDatabase;

namespace AlternativeBans
{
    [ApiVersion(2, 1)]
    public class PluginMain : TerrariaPlugin
    {
        public override string Name => "Alternative Bans";

        public override Version Version => new(1, 0);

        public override string Author => "rusty";

        public static BansDatabase Bans { get; private set; }

        public static BanConfig Config { get; private set; }

        private static readonly string[] _lastListIdentifier = new string[Main.maxPlayers];

        public PluginMain(Main game) : base(game)
        {
            Order = -1;
        }

        public override void Initialize()
        {
            ServerApi.Hooks.ServerJoin.Register(this, OnJoin);
            TShock.Initialized += OnTShockInit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {

                ServerApi.Hooks.ServerJoin.Deregister(this, OnJoin);
                TShock.Initialized -= OnTShockInit;
                TShockAPI.Hooks.GeneralHooks.ReloadEvent -= OnReload;
                TShockAPI.Hooks.PlayerHooks.PlayerPostLogin -= OnPostLogin;
            }

            base.Dispose(disposing);
        }

        private static string GetBanTime(DateTime expiration)
        {
            var span = expiration - DateTime.UtcNow;
            var date = "";

            if (span.Days > 0)
                date += string.Format("{0} day{1} ", span.Days, span.Days == 1 ? "" : "s");
            if (span.Hours > 0)
                date += string.Format("{0} hour{1} ", span.Hours, span.Hours == 1 ? "" : "s");
            if (span.Minutes > 0)
                date += string.Format("{0} minute{1} ", span.Minutes, span.Minutes == 1 ? "" : "s");
            if (span.Seconds > 0)
                date += string.Format("{0} second{1} ", span.Seconds, span.Seconds == 1 ? "" : "s");

            return date.Remove(date.Length - 1);
        }

        private static void BanDisconnect(TSPlayer plr, string reason, DateTime expiration, int id = -1)
        {

            //proxy stuff, we disconnect players to a server in our network.
            string banned = "banned";

            if (plr == null)
                return;

            var date = "This ban is permanent.";

            if (expiration.Year != 9999)
                date = "Expires in " + GetBanTime(expiration);

            plr.SendErrorMessage(string.Format("You have been banned{2}.\nReason: {0}\n{1}", reason, date, id == -1 ? "" : ". ID: " + id));

            byte[] data = (new PacketFactory())
                    .SetType(67)
                    .PackInt16(2)
                    .PackString(banned)
                    .GetByteData();

            plr.SendRawData(data);
        }

        private void HandleBan(CommandArgs args, AltBanCMD_IdentifierType type)
        {
            var length = DateTime.MaxValue; //ban add user server time reason
            var server = "all";
            var reason = "No reason specified";
            var identifier = args.Parameters[1];
            var hasTime = false;
            var hasServer = false;

            for (int i = 2; i < args.Parameters.Count; i++)
            {
                if (!hasServer && Config.UseDimensions && Config.BannableDimensions.Any(s => s.ToLowerInvariant() == args.Parameters[i].ToLowerInvariant()))
                {
                    server = args.Parameters[i].ToLowerInvariant();
                    hasServer = true;
                }
                else if (!hasTime && TShock.Utils.TryParseTime(args.Parameters[i], out ulong seconds))
                {
                    length = DateTime.UtcNow.AddSeconds(seconds);
                    hasTime = true;
                }
                else
                {
                    reason = string.Join(" ", args.Parameters.Skip(i));
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(reason))
                reason = "No reason specified.";

            switch (type)
            {
                case AltBanCMD_IdentifierType.UUID:
                    {
                        var account = TShock.UserAccounts.GetUserAccountByName(identifier);

                        if (account == null && int.TryParse(identifier, out var accId))
                            account = TShock.UserAccounts.GetUserAccountByID(accId);

                        if (Bans.AddBan(args.Player.Name, reason, length, server, "", "", "", account == null ? identifier : account.UUID))
                        {
                            BanDisconnect(TShock.Players.FirstOrDefault(p => p != null && p.UUID == (account == null ? identifier : account.UUID)), reason, length);
                            args.Player.SendSuccessMessage("Banned {0}", account == null ? string.Format("UUID {0}", identifier) : string.Format("{0}'s UUID.", account.Name));
                        }
                        else
                            args.Player.SendErrorMessage("Ban failed.");
                    }
                    break;

                case AltBanCMD_IdentifierType.Name:
                    {
                        if (Bans.AddBan(args.Player.Name, reason, length, server, "", identifier, "", ""))
                        {
                            BanDisconnect(TShock.Players.FirstOrDefault(p => p != null && p.Name == identifier), reason, length);
                            args.Player.SendSuccessMessage("Banned joining with the name {0}.", identifier);
                        }
                        else
                            args.Player.SendErrorMessage("Ban failed.");
                    }
                    break;

                case AltBanCMD_IdentifierType.IP:
                    {
                        var account = TShock.UserAccounts.GetUserAccountByName(identifier);
                        var isIp = IPAddress.TryParse(identifier, out var ip);

                        if (account == null && int.TryParse(identifier, out var accId))
                            account = TShock.UserAccounts.GetUserAccountByID(accId);

                        if (account == null && !isIp)
                        {
                            args.Player.SendErrorMessage("Invalid account or IP.");
                            return;
                        }

                        if (Bans.AddBan(args.Player.Name, reason, length, server, "", "", isIp ? identifier :
                            JsonConvert.DeserializeObject<List<string>>(account.KnownIps).Last(), ""))
                        {
                            BanDisconnect(TShock.Players.FirstOrDefault(p => (p != null && p.IP == identifier) || (p.Account != null && account != null
                            && p.Account.ID == account.ID)), reason, length);
                            args.Player.SendSuccessMessage("Banned {0}.", isIp ? identifier : string.Format("{0}'s IP.", account.Name));
                        }
                        else
                            args.Player.SendErrorMessage("Ban failed.");
                    }
                    break;

                case AltBanCMD_IdentifierType.AccountName:
                case AltBanCMD_IdentifierType.Account:
                    {
                        var account = TShock.UserAccounts.GetUserAccountByName(identifier);
                        var nameOnly = type == AltBanCMD_IdentifierType.AccountName;

                        if (account == null && int.TryParse(identifier, out var accId))
                            account = TShock.UserAccounts.GetUserAccountByID(accId);

                        if (account == null)
                        {
                            args.Player.SendErrorMessage("Invalid account.");
                            return;
                        }

                        var res = false;

                        if (nameOnly)
                            res = Bans.AddBan(args.Player.Name, reason, length, server, account.Name);
                        else
                            res = Bans.AddBan(args.Player.Name, reason, length, server, account.Name, "", JsonConvert.DeserializeObject<List<string>>(account.KnownIps).Last(),
                                account.UUID);


                        if (res)
                        {
                            BanDisconnect(TShock.Players.FirstOrDefault(p => p != null && p.IsLoggedIn && p.Account.ID == account.ID), reason, length);

                            var message = string.Format("{0} banned {1} for {2} {3}.", args.Player.Name, account.Name, reason, length == DateTime.MaxValue ?
                                    "permanently" : string.Format("for {0}", GetBanTime(length)));

                            if (args.Silent)
                                args.Player.SendSuccessMessage(message);
                            else
                                TSPlayer.All.SendInfoMessage(message);
                        }
                        else
                            args.Player.SendErrorMessage("Ban failed.");
                    }
                    break;

                case AltBanCMD_IdentifierType.Player:
                    {
                        var plrs = TSPlayer.FindByNameOrID(identifier);

                        if (!plrs.Any())
                        {
                            HandleBan(args, AltBanCMD_IdentifierType.Account);
                            return;
                        }

                        if (plrs.Count > 1)
                        {
                            args.Player.SendMultipleMatchError(plrs.Select(p => p.Name));
                            return;
                        }

                        if (Bans.AddBan(plrs.First(), args.Player.Account.Name, reason, length, server))
                        {
                            BanDisconnect(plrs.First(), reason, length);

                            var message = string.Format("{0} banned {1} for {2} {3}.", args.Player.Name, plrs.First().Name, reason, length == DateTime.MaxValue ?
                                    "permanently" : string.Format("for {0}", GetBanTime(length)));

                            if (args.Silent)
                                args.Player.SendSuccessMessage(message);
                            else
                                TSPlayer.All.SendInfoMessage(message);
                        }
                        else
                            args.Player.SendErrorMessage("Ban failed.");
                    }
                    break;
            }
        }

        private void ConvertCMD(CommandArgs args)
        {
            if (args.Player is TSServerPlayer)
            {
                args.Player.SendSuccessMessage("Starting conversion...");
                Bans.ConvertWeirdoBans();
                args.Player.SendSuccessMessage("Converted.");
            }
            else
                args.Player.SendErrorMessage("This command is only accessible in console.");
        }

        private void AltBanCMD(CommandArgs args)
        {
            var subCmd = "commands";

            if (args.Parameters.Any())
                subCmd = args.Parameters[0];

            switch (subCmd)
            {
                case "commands":
                case "cmds":
                case "c":
                    {
                        args.Player.SendSuccessMessage("AltBans Commands:");
                        args.Player.SendInfoMessage("{0} --Explains syntaxes and shortcuts.", "/ban help".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Lists commands.", "/ban commands".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Explains {1}.", "/ban listhelp".Color(Color.White), "/ban list".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Adds a ban via {1} identifiers with an optional ban length and reason.",
                            "/ban add (player/account name) (length = permanent) (reason = No reason specified.)".Color(Color.White),
                            Config.DefaultBanType);
                        args.Player.SendInfoMessage("{0} --Bans an account's uuid or a raw uuid hash with an optional ban length and reason.",
                            "/ban adduuid (account name/uuid hash) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Bans an account's ip or a raw ip with an optional ban length and reason.",
                            "/ban addip (account name/ip) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Bans an account via name with an optional ban length and reason.",
                            "/ban addaccount (account name/id) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Bans a character name with an optional ban length and reason.",
                            "/ban addname (name) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Lists bans. See {1}.", "/ban list (filter = none)".Color(Color.White), "/ban listhelp".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Deletes all bans that contain any identifier that matches the provided text. Includes IDs.",
                            "/ban delall (identifier)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Deletes a ban via ID.", "/ban delid (id)".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Deletes a ban via account name. (old /ban del)", "/ban del (account name/id)".Color(Color.White));
                        args.Player.SendInfoMessage("You can scroll this message with your up/down arrow keys.");
                    }
                    break;

                case "help":
                    {
                        args.Player.SendSuccessMessage("Alternative Bans");
                        args.Player.SendInfoMessage("Syntaxes are displayed differently in AltBans. (length = permanent) means if there is no length given, it will be permanent.");
                        args.Player.SendInfoMessage("AltBans allows all parameters to be optional except for identifiers. See examples below.");
                        args.Player.SendInfoMessage("AltBans also has shortcuts for all subcommands. Shortcuts are the first letters of each word eg. addname = an");
                        args.Player.SendInfoMessage("AltBans also has native dimensions support. If support is enabled, a dimension can be specified in the ban.");
                        if (Config.UseDimensions)
                            args.Player.SendInfoMessage("Bannable dimensions: {0}", string.Join(", ", Config.BannableDimensions));
                        args.Player.SendInfoMessage("Examples:");
                        args.Player.SendInfoMessage("{0} --Permanently bans IP 127.0.0.1 with the reason ''go away''.", "/ban addip 127.0.0.1 go away".Color(Color.White));
                        args.Player.SendInfoMessage("{0} --Bans {1} identifiers of Deez Nuts for 3 days for ''bad humor''", "/ban add ''Deez Nuts'' 3d bad humor".Color(Color.White),
                            Config.DefaultBanType);
                        args.Player.SendInfoMessage("{0} --Bans Cy's UUID on the dimension ''Freebuild'' for 1 hour for ''idling''", "/ban adduuid Cy freebuild 1h idling".Color(Color.White));
                    }
                    break;

                case "listhelp":
                    {
                        args.Player.SendSuccessMessage("AltBans List Help");
                        args.Player.SendInfoMessage("Bans can be sorted by author, account name, IP, name, server (if using dimensions), ID, UUID hash or date.");
                        args.Player.SendInfoMessage("Date can be searched by providing MM/DD/YYYY (month, day, year).");
                        args.Player.SendInfoMessage("It will not display results if more than 10 results are found. This is bypassed via executing through a Discord Bridge.");
                    }
                    break;

                case "add":
                case "a":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Invalid syntax. Proper syntax {0}.",
                                "/ban add (player/account name) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                            return;
                        }

                        HandleBan(args, AltBanCMD_IdentifierType.Player);
                    }
                    break;

                case "adduuid":
                case "au":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Invalid syntax. Proper syntax: {0}.",
                                "/ban adduuid (account name/uuid hash) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                            return;
                        }

                        HandleBan(args, AltBanCMD_IdentifierType.UUID);
                    }
                    break;

                case "addip":
                case "ai":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Invalid syntax. Proper syntax: {0}.",
                                "/ban addip (account name/ip) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                            return;
                        }

                        HandleBan(args, AltBanCMD_IdentifierType.IP);
                    }
                    break;

                case "addaccount":
                case "aa":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Invalid syntax. Proper syntax: {0}.",
                                "/ban addaccount (account name/id) (length = permanent) (reason = No reason specified.)".Color(Color.White));
                            return;
                        }

                        HandleBan(args, AltBanCMD_IdentifierType.AccountName);
                    }
                    break;

                case "addname":
                case "an":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Invalid syntax. Proper syntax: {0}.", "/ban addname (name) (length = permanent) (reason = No reason specified.)"
                                .Color(Color.White));
                            return;
                        }

                        HandleBan(args, AltBanCMD_IdentifierType.Name);
                    }
                    break;

                case "list":
                case "l":
                    {
                        if (args.Player is TSServerPlayer)
                        {
                            args.Player.SendErrorMessage("Listing does not work in console.");
                            return;
                        }

                        var bans = Bans.GetBans();
                        var date = DateTime.MaxValue;
                        string identifier = null;
                        var page = 1;

                        if (args.Player.RealPlayer)
                        {
                            if (args.Parameters.Count > 1 && _lastListIdentifier[args.Player.Index] != string.Join(" ", args.Parameters.Skip(1)))
                                identifier = _lastListIdentifier[args.Player.Index] = string.Join(" ", args.Parameters.Skip(1));
                            else
                            {
                                identifier = _lastListIdentifier[args.Player.Index];
                                if (args.Parameters.Count == 2 && int.TryParse(args.Parameters[1], out var i))
                                    page = i;
                            }
                        }
                        else // awful
                        {
                            if (args.Parameters.Count > 2)
                            {
                                if (int.TryParse(args.Parameters[1], out var i))
                                    page = i;
                                else
                                {
                                    args.Player.SendErrorMessage("Page must be a number.");
                                    return;
                                }

                                identifier = string.Join(" ", args.Parameters.Skip(2));
                            }
                        }

                        if (identifier != null && !DateTime.TryParse(identifier, out date))
                            date = DateTime.MaxValue;

                        var sortedBans = identifier == null ? bans : bans.FindAll(ban => ban != null &&
                            ((ban.AccountName != null && ban.AccountName.ToLowerInvariant().StartsWith(identifier.ToLowerInvariant()))
                            || (ban.Author != null && ban.Author.ToLowerInvariant().StartsWith(identifier.ToLowerInvariant()))
                            || ban.IP == identifier
                            || ban.UUID == identifier
                            || (ban.Server != null && ban.Server.ToLowerInvariant() == identifier.ToLowerInvariant())
                            || ban.ID.ToString() == identifier
                            || (ban.Name != null && ban.Name.ToLowerInvariant().StartsWith(identifier.ToLowerInvariant()))
                            || (date != DateTime.MaxValue && date.Day == ban.BanDate.Day && date.Month == ban.BanDate.Month && date.Year == ban.BanDate.Year)));

                        if (Config.DeleteExpiredBansFoundInListCommand)
                        {
                            foreach (var ban in sortedBans.ToList())
                            {
                                if (DateTime.UtcNow >= ban.Expiration)
                                {
                                    sortedBans.Remove(ban);
                                    Bans.DeleteBan(ban.ID);
                                }
                            }
                        }

                        var settings = new PaginationTools.Settings()
                        {
                            HeaderFormat = "Ban list {0}/{1}",
                            FooterFormat = "Type /ban list {0} for more.",
                            NothingToDisplayString = "No bans.",
                            MaxLinesPerPage = args.Player.RealPlayer ? Config.MaxBansPerPageIngame : Config.MaxBansPerPageDiscord,
                        };

                        if (!args.Player.RealPlayer)
                            settings.LineFormatter = (line, ix, p) =>
                            {
                                if (ix == 0)
                                    return new Tuple<string, Color>(string.Format("{0}```", line), Color.Yellow);

                                return new Tuple<string, Color>(string.Format("```{0}```", line), Color.Yellow);
                            };

                        PaginationTools.SendPage(args.Player, page, sortedBans, settings);
                    }
                    break;

                case "delall":
                case "da":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Please specify a UUID, IP, ID, account name or name.");
                            return;
                        }

                        var amount = Bans.DeleteBan(string.Join(" ", args.Parameters.Skip(1)));

                        if (amount > 0)
                            args.Player.SendSuccessMessage("Deleted {0} bans.", amount);
                        else
                            args.Player.SendErrorMessage("No bans found with the identifier ''{0}''", string.Join(" ", args.Parameters.Skip(1)));
                    }
                    break;

                case "delid":
                case "di":
                case "did":
                    {
                        if (args.Parameters.Count == 1 || !int.TryParse(args.Parameters[1], out var id))
                        {
                            args.Player.SendErrorMessage("Please specify a numerical ID.");
                            return;
                        }

                        var ban = Bans.GetBan(id);

                        if (ban == null)
                        {
                            args.Player.SendErrorMessage("Invalid ban.");
                            return;
                        }

                        if (Bans.DeleteBan(id) == 1)
                            args.Player.SendSuccessMessage("Unbanned {0}.", ban.ToString());
                        else
                            args.Player.SendErrorMessage("Unban failed.");
                    }
                    break;

                case "del":
                case "d":
                    {
                        if (args.Parameters.Count == 1)
                        {
                            args.Player.SendErrorMessage("Please specify an account name.");
                            return;
                        }

                        var account = TShock.UserAccounts.GetUserAccountByName(string.Join(" ", args.Parameters.Skip(1)));

                        if (account == null && int.TryParse(args.Parameters[1], out var accId))
                            account = TShock.UserAccounts.GetUserAccountByID(accId);

                        if (account == null)
                        {
                            args.Player.SendErrorMessage("Invalid account.");
                            return;
                        }

                        var ban = Bans.GetBan(null, null, null, account.Name);

                        if (ban == null)
                        {
                            args.Player.SendErrorMessage("{0} has no bans.", account.Name);
                            return;
                        }

                        if (Bans.DeleteBan(ban.ID) == 1)
                            args.Player.SendSuccessMessage("Unbanned {0}.", ban.ToString());
                        else
                            args.Player.SendErrorMessage("Unban failed.");
                    }
                    break;

                default:
                    args.Player.SendErrorMessage("Invalid subcommand. Type {0} for subcommands.", "/ban help".Color(Color.White));
                    break;
            }
        }

        private void OnReload(TShockAPI.Hooks.ReloadEventArgs args)
        {
            Config = BanConfig.Read();
            args.Player.SendSuccessMessage("Reloaded AltBans config.");
        }

        private void OnPostLogin(TShockAPI.Hooks.PlayerPostLoginEventArgs args)
        {
            var player = args.Player;
            var bans = Bans.GetBans(player.UUID, player.IP, string.IsNullOrWhiteSpace(player.Name) ? null : player.Name, player.Account.Name);
            var newestBanDate = DateTime.MinValue;
            AltBan newestBan = null;

            foreach (var ban in bans)
            {
                if (DateTime.UtcNow >= ban.Expiration)
                {
                    Bans.DeleteBan(ban.ID);
                    continue;
                }

                if (ban.BanDate >= newestBanDate && (!Config.UseDimensions || ban.Server == "all" || ban.Server == Config.DimensionName))
                {
                    newestBan = ban;
                    newestBanDate = ban.BanDate;
                }
            }

            if (newestBan != null)
                BanDisconnect(player, newestBan.Reason, newestBan.Expiration, newestBan.ID);
        }

        private void OnTShockInit()
        {
            Config = BanConfig.Read();
            Bans = new BansDatabase();

            if (Config.DeleteExpiredBansOnServerStart)
            {
                foreach (var ban in Bans.GetBans())
                {
                    if (DateTime.UtcNow >= ban.Expiration)
                        Bans.DeleteBan(ban.ID);
                }
            }

            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += OnPostLogin;
            TShockAPI.Hooks.GeneralHooks.ReloadEvent += OnReload;
            Commands.ChatCommands.Find(c => c.Name == "ban").CommandDelegate = AltBanCMD;

            if (!Config.DisableConvertCommand)
                Commands.ChatCommands.Add(new Command("altbans.dev.convert", ConvertCMD, "converttshockbans"));
        }

        private void OnJoin(JoinEventArgs args)
        {
            var player = TShock.Players[args.Who];

            if (player == null)
                return;

            var bans = Bans.GetBans(player.UUID, player.IP, string.IsNullOrWhiteSpace(player.Name) ? null : player.Name);
            var newestBanDate = DateTime.MinValue;
            AltBan newestBan = null;

            foreach (var ban in bans)
            {
                if (DateTime.UtcNow >= ban.Expiration)
                {
                    Bans.DeleteBan(ban.ID);
                    continue;
                }

                if (ban.BanDate >= newestBanDate && (!Config.UseDimensions || ban.Server == "all" || ban.Server == Config.DimensionName))
                {
                    newestBan = ban;
                    newestBanDate = ban.BanDate;
                }
            }

            if (newestBan != null)
            {
                BanDisconnect(player, newestBan.Reason, newestBan.Expiration, newestBan.ID);
                args.Handled = true;
            }
        }

        private enum AltBanCMD_IdentifierType
        {
            Name,
            AccountName,
            UUID,
            IP,
            Player,
            Account
        }
    }
}
