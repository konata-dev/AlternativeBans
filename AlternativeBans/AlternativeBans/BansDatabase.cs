using Microsoft.Xna.Framework;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;
using TShockAPI.DB;

namespace AlternativeBans
{
	public class BansDatabase 
	{
		private IDbConnection _db;

		public BansDatabase()
        {
			if (TShock.Config.Settings.StorageType.Equals("mysql", StringComparison.OrdinalIgnoreCase))
			{
				string[] host = PluginMain.Config.SQLHost.Split(':');
				_db = new MySqlConnection()
				{
					ConnectionString = String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
					host[0],
					host.Length == 1 ? "3306" : host[1],
					PluginMain.Config.SQLDatabaseName,
					PluginMain.Config.SQLUsername,
					PluginMain.Config. SQLPassword)
				};
			}
			else if (TShock.Config.Settings.StorageType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
				_db = new SqliteConnection(String.Format("uri=file://{0},Version=3",
					Path.Combine(TShock.SavePath, "altbans.sqlite")));
			else
				throw new InvalidOperationException("Invalid storage type!");

			new SqlTableCreator(_db, TShockAPI.TShock.Config.Settings.StorageType == "mysql" ? new MysqlQueryCreator() : (IQueryBuilder)new SqliteQueryCreator())
				.EnsureTableStructure(new SqlTable("altbans",
				new SqlColumn("id", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
				new SqlColumn("name", MySqlDbType.Text),
				new SqlColumn("accountname", MySqlDbType.Text),
				new SqlColumn("reason", MySqlDbType.Text),
				new SqlColumn("author", MySqlDbType.Text),
				new SqlColumn("uuid", MySqlDbType.Text),
				new SqlColumn("expiration", MySqlDbType.Text),
				new SqlColumn("date", MySqlDbType.Text),
				new SqlColumn("ip", MySqlDbType.Text),
				new SqlColumn("server", MySqlDbType.Text)
				));
		}

		internal void ConvertWeirdoBans()
        {
			try
            {
				var weirdoBans = TShock.Bans.Bans;
				var dates = new List<DateTime>();

				foreach (var ban in weirdoBans) // pepega
                {
					if (!dates.Contains(ban.Value.BanDateTime))
						dates.Add(ban.Value.BanDateTime);
                }

				AltBan newBan = null;

				foreach (var date in dates) // in theory, you can get the original bans by comparing dates as the chances of 2 people being banned in the same second is tiny
                {
					var bans = weirdoBans.Values.Where(b => b.BanDateTime == date);

					foreach (var ban in bans)
                    {
						if (newBan == null)
						{
							newBan = new AltBan()
							{
								BanDate = ban.BanDateTime,
								Author = ban.BanningUser,
								Expiration = ban.ExpirationDateTime,
								Reason = ban.Reason,
								Server = "all",
								AccountName = "",
								IP = "",
								Name = "",
								UUID = ""
							};
						}

						var identifier = ban.Identifier.Split(':');

						switch (identifier[0])
                        {
							case "acc":
								newBan.AccountName = identifier[1];
								break;

							case "ip":
								newBan.IP = identifier[1];
								break;

							case "uuid":
								newBan.UUID = identifier[1];
								break;

							case "name":
								newBan.Name = identifier[1];
								break;
                        }
                    }

					AddBan(newBan);
					newBan = null;
                }

				if (TShock.Config.Settings.StorageType == "mysql")
					_db.Query("ALTER TABLE playerbans RENAME TO playerbansbackup;");
				else
					TShock.Log.ConsoleError("You are not using sqlite. You will need to manually rename ''playerbans'' to ''playerbansbackup'' or something" +
						"\nYOU NEED TO DO THIS OR TSHOCKS BANS WILL STILL BE PRESENT. Google how to do it or ping rusty#1000 on the tshock discord.");
            }
			catch (Exception ex)
            {
				TShock.Log.ConsoleError(ex.ToString());
            }
        }

		internal void ConvertBansT1()
        {
			try
            {
				using (var reader = _db.QueryReader("SELECT * FROM bans"))
                {
					while (reader.Read())
                    {
						var date = reader.Get<string>("Expiration");
						AddBan(reader.Get<string>("BanningUser"), reader.Get<string>("Reason"), string.IsNullOrWhiteSpace(date) ? DateTime.MaxValue : DateTime.Parse(date),
							reader.Get<string>("Server"), reader.Get<string>("Name"), reader.Get<string>("Name"), reader.Get<string>("IP"), reader.Get<string>("UUID"),
							DateTime.Parse(reader.Get<string>("Date")));
                    }
                }
            }
			catch (Exception ex)
            {
				TShock.Log.ConsoleError(ex.ToString());
            }
        }

		public bool AddBan(AltBan ban)
        {
			return AddBan(ban.Author, ban.Reason, ban.Expiration, ban.Server, ban.AccountName, ban.Name, ban.IP, ban.UUID);
        }

		internal bool AddBan(TSPlayer target, string author, string reason, DateTime expiration, string server = "all")
        {
			switch (PluginMain.Config.DefaultBanType)
            {
				default:
					return AddBan(author, reason, expiration, server, target.IsLoggedIn ? target.Account.Name : "", target.Name, target.IP, target.UUID);

				case "uuid":
					return AddBan(author, reason, expiration, server, "", "", "", target.UUID);

				case "ip":
					return AddBan(author, reason, expiration, server, "", "", target.IP, "");

				case "name":
					return AddBan(author, reason, expiration, server, "", target.Name, "", "");

				case "account":
					if (!target.IsLoggedIn)
						return false;
					return AddBan(author, reason, expiration, server, target.Account.Name, "", "", "");
			}
        }

		public bool AddBan(string author, string reason, DateTime expiration, string server = "all", string accountName = "", string name = "", string ip = "", string uuid = "",
			DateTime? date = null)
        {
			try
            {
				var actualDate = DateTime.Now;
				if (date.HasValue)
					actualDate = date.Value;

				if (_db.Query("INSERT INTO altbans (name, accountname, reason, author, uuid, expiration, date, ip, server) VALUES (@0, @1, @2, @3, @4, @5, @6, @7, @8)",
					name, accountName, reason, author, uuid, expiration.ToString("s"), actualDate.ToString("s"), ip, server.ToLowerInvariant()) == 1)
					return true;
            }
			catch (Exception ex)
            {
				TShock.Log.ConsoleError(ex.ToString());
            }

			return false;
        }

		public int DeleteBan(string identifier)
        {
			try
			{
				return _db.Query("DELETE FROM altbans WHERE name = @0 OR accountname = @0 OR uuid = @0 OR ip = @0", identifier);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return 0;
		}

		public int DeleteBan(int id)
		{
			try
			{
				return _db.Query("DELETE FROM altbans WHERE id = @0", id);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return 0;
		}

		public List<AltBan> GetBans()
        {
			var output = new List<AltBan>();

			try
			{
				using (var reader = _db.QueryReader("SELECT * FROM altbans"))
				{
					while (reader.Read())
					{
						output.Add(new AltBan()
						{
							AccountName = reader.Get<string>("accountname"),
							Author = reader.Get<string>("author"),
							BanDate = DateTime.Parse(reader.Get<string>("date")),
							Expiration = DateTime.Parse(reader.Get<string>("expiration")),
							ID = reader.Get<int>("id"),
							IP = reader.Get<string>("ip"),
							Reason = reader.Get<string>("reason"),
							Name = reader.Get<string>("name"),
							UUID = reader.Get<string>("uuid"),
							Server = reader.Get<string>("server")
						});
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return output;
		}

		public AltBan GetBan(int id)
        {
			try
			{
				using (var reader = _db.QueryReader("SELECT * FROM altbans WHERE id = @0", id))
				{
					if (reader.Read())
					{
						return new AltBan()
						{
							AccountName = reader.Get<string>("accountname"),
							Author = reader.Get<string>("author"),
							BanDate = DateTime.Parse(reader.Get<string>("date")),
							Expiration = DateTime.Parse(reader.Get<string>("expiration")),
							ID = reader.Get<int>("id"),
							IP = reader.Get<string>("ip"),
							Reason = reader.Get<string>("reason"),
							Name = reader.Get<string>("name"),
							UUID = reader.Get<string>("uuid"),
							Server = reader.Get<string>("server")
						};
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return null;
		}

		public List<AltBan> GetBans(string uuid, string ip = null, string name = null, string accountName = null)
        {
			var output = new List<AltBan>();

			try
			{
				using (var reader = _db.QueryReader("SELECT * FROM altbans WHERE name = @0 OR accountname = @1 OR uuid = @2 OR ip = @3", name,
					accountName, uuid, ip))
				{
					if (reader.Read())
					{
						output.Add(new AltBan()
						{
							AccountName = reader.Get<string>("accountname"),
							Author = reader.Get<string>("author"),
							BanDate = DateTime.Parse(reader.Get<string>("date")),
							Expiration = DateTime.Parse(reader.Get<string>("expiration")),
							ID = reader.Get<int>("id"),
							IP = reader.Get<string>("ip"),
							Reason = reader.Get<string>("reason"),
							Name = reader.Get<string>("name"),
							UUID = reader.Get<string>("uuid"),
							Server = reader.Get<string>("server")
						});
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return output;
		}

		public AltBan GetBan(string uuid = null, string ip = null, string name = null, string accountName = null)
        {
			try
			{
				using (var reader = _db.QueryReader("SELECT * FROM altbans WHERE name = @0 OR accountname = @1 OR uuid = @2 OR ip = @3", name,
					accountName, uuid, ip))
				{
					if (reader.Read())
					{
						return new AltBan()
						{
							AccountName = reader.Get<string>("accountname"),
							Author = reader.Get<string>("author"),
							BanDate = DateTime.Parse(reader.Get<string>("date")),
							Expiration = DateTime.Parse(reader.Get<string>("expiration")),
							ID = reader.Get<int>("id"),
							IP = reader.Get<string>("ip"),
							Reason = reader.Get<string>("reason"),
							Name = reader.Get<string>("name"),
							UUID = reader.Get<string>("uuid"),
							Server = reader.Get<string>("server")
						};
					}
				}
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError(ex.ToString());
			}

			return null;
		}

		public AltBan GetBan(string identifier)
        {
			try
            {
				using (var reader = _db.QueryReader("SELECT * FROM altbans WHERE name = @0 OR accountname = @0 OR uuid = @0 OR ip = @0 OR id = @0", identifier))
                {
					if (reader.Read())
					{
						return new AltBan()
						{
							AccountName = reader.Get<string>("accountname"),
							Author = reader.Get<string>("author"),
							BanDate = DateTime.Parse(reader.Get<string>("date")),
							Expiration = DateTime.Parse(reader.Get<string>("expiration")),
							ID = reader.Get<int>("id"),
							IP = reader.Get<string>("ip"),
							Reason = reader.Get<string>("reason"),
							Name = reader.Get<string>("name"),
							UUID = reader.Get<string>("uuid"),
							Server = reader.Get<string>("server")
						};
					}
                }
            }
			catch (Exception ex)
            {
				TShock.Log.ConsoleError(ex.ToString());
            }

			return null;
        }

		public class AltBan
        {
			public int ID { get; set; } = -1;

			public string Name { get; set; }

			public string AccountName { get; set; }

			public string Reason { get; set; } = "No reason provided.";

			public string Author { get; set; } = "Unknown";

			public string UUID { get; set; }

			public DateTime Expiration { get; set; } = DateTime.MaxValue;

			public DateTime BanDate { get; set; } = DateTime.Now;

			public string IP { get; set; }

			public string Server { get; set; } = "all";

            public override string ToString()
            {
                var strs = new List<string>
                {
                    string.Format("ID: {0}", ID.Color(Color.Red)),
                    string.Format("Reason: {0}", Reason.Color(Color.White)),
                    string.Format("Author: {0}", (string.IsNullOrWhiteSpace(Author) ? "Unknown" : Author).Color(Color.SkyBlue)),
                    string.Format("Expiration: {0}", (Expiration.Year == 9999 ? "Permanent" : Expiration.ToString("g")).Color(Color.Red)),
                    string.Format("Date: {0}", BanDate.ToString("g").Color(Color.Red))
                };

                if (PluginMain.Config.UseDimensions)
					strs.Add(string.Format("Server: {0}", Server.Color(Color.SkyBlue)));

				if (!string.IsNullOrWhiteSpace(Name))
					strs.Add(string.Format("Name: {0}", Name.Color(Color.White)));

				if (!string.IsNullOrWhiteSpace(AccountName))
					strs.Add(string.Format("Account: {0}", AccountName.Color(Color.White)));

				if (!string.IsNullOrWhiteSpace(IP))
					strs.Add(string.Format("IP: {0}", IP.Color(Color.White)));

				return string.Join(", ", strs);
            }
        }
	}
}
