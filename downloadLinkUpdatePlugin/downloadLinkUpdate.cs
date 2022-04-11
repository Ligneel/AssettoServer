using AssettoServer.Server;
using AssettoServer.Server.Plugin;
using AssettoServer.Server.Configuration;
using System;
using System.Net;
using System.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using AssettoServer.Network.Packets.Shared;
using System.Drawing;
using System.IO;
using AssettoServer.Network.Packets.Outgoing;
using AssettoServer.Network.Packets;
using AssettoServer.Network.Packets.Outgoing;
using AssettoServer.Network.Packets.Outgoing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Server.Ai;
using System.Collections.Concurrent;
using NanoSockets;
using AssettoServer.Commands.TypeParsers;
using System.Reflection;
using AssettoServer.Server.TrackParams;
using System;
using System.Threading;
using System.Threading.Tasks;
//using TimeZoneConverter;

namespace downloadLinkUpdatePlugin;

/// <summary>
/// DISCLAIMER : I don't know programming and I have no idea what I'm doing. I just tried to put stuff together with limited understanding. 
/// Please don't cry when you  see my code, it's going to be alright.
/// </summary>



public class downloadLinkUpdatePlugin : IAssettoServerPlugin
{
    ACServer _server = null!;
    public ACClientTypeParser? _ACClientTypeParser=null!;
    bool sessionEndingSoon = false;
    bool serverRestartMessageSent = false;
    EntryCar[] OriginalEntryCars = null!;
    string trackLayoutName = "";
    int pitboxes = 0;
    string track_json_path = "";
    string trackDownloadLink = "";
    JArray trackList = null!;
    EntryList originalEntryList = null!;
    EntryList dynamicEntryList = null!;
    //add welcome message
    long lastSessionstartTime;
    string configWelcomeMessage = "";//stores the welcome message from the config file
    string aboutServer = "REQUIRES CSP 1.77 OR ABOVE FOR AUTO RE-JOIN ON TRACK CHANGE.\n\nAbout the server:\nCurrently in devellopement. Based on the custom ACServer made by Compu & friends. The main change here is mainly from a plugin I wrote to change track randomly and automatically edit the server description to provide download links from a google spreadsheet. For some things, I've had to edit the original server's code. The Files are available at https://github.com/Ligneel/AssettoServer-trackcycle. If you wish the most up to date one it might be best to hit me up on discord though.";







    public static bool NextTrackIsSpecific { get; set; } = false;
    public static string nextTrack { get; set; } = "";


    public void Initialize(ACServer server)
    {    
        _server = server;
        originalEntryList = server.Configuration.EntryList;//not sure if usefull
        OriginalEntryCars = _server.EntryCars;
      
        trackList = JArray.Parse(getSpreadsheetData()); // update trackList (parse the json string as array  //put as var?)
        UpdateServerTrack();
        UpdateServerInfo(ref trackDownloadLink, ref configWelcomeMessage, ref aboutServer);
        

        Log.Debug("adding Online Event: serverReconnectEvent ");
        _server.CSPLuaClientScriptProvider.AddLuaClientScript("local serverReconnectEvent = ac.OnlineEvent({    message = ac.StructItem.string(16)  }, function (sender, data)    if data.message:match('ReconnectClients') and (sender and sender.index or -1) == -1 then        ac.reconnectTo({carID = ac.getCarID(0)})          return true        end    end)", "");

        Log.Debug("downloadLinkUpdate plugin initialized");
        //_server.features.Add("FREQUENT_TRACK_CHANGES");
        _ = LoopAsync();
    }




  
    internal async Task LoopAsync()  ///to run the function every x milliseconds
    {
        while (true)
        {
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during trackcycle plugin run");
            }
            finally
            {
                await Task.Delay(1000);
            }
        }
    }

   
    public void Update() ///sends a message to players once the session is close to ending
    {
        if (_server != null)
        {
            long CurrentSessionElapsedTime = _server.CurrentTime - _server.CurrentSession.StartTimeTicks64;
            long sessionTime = (60_000 * _server.CurrentSession.Configuration.Time);

            if (lastSessionstartTime != _server.CurrentSession.StartTimeTicks64)//if end of session update track and reconnect clients
            {
                trackList = JArray.Parse(getSpreadsheetData()); // update trackList (parse the json string as array  //put as var?)
                UpdateServerTrack();
                reconnectClientsOnTrackChange();
                UpdateServerInfo(ref trackDownloadLink, ref configWelcomeMessage, ref aboutServer);
                Log.Information("Updating server to new track (" + nextTrack + ").");
                ((Func<Task>)(async () => {
                    await Task.Delay(3000);                    
                    reconnectClientsOnTrackChange();
                }))();
                _server.BroadcastPacket(new ChatMessage { SessionId = 255, Message = "Track changed, please rejoin." });// in case reconnectClientsOnTrackChange doesn't work
                lastSessionstartTime = _server.CurrentSession.StartTimeTicks64;//might not work, used to be in initialise when initialised was called on track change
            }
            else
            {
                if (CurrentSessionElapsedTime > (sessionTime - 30_000))/// if session ends soon 
                {
                    sessionEndingSoon = true;
                }
                else { sessionEndingSoon = false; }
                if (sessionEndingSoon == false)//on a newer session, resets serveRestartMessageSent
                {
                    serverRestartMessageSent = false;
                }
                if (sessionEndingSoon && serverRestartMessageSent == false)///check if session ending message has been sent, if not send it (to prevent the message from being sent multiple times)
                {
                    Log.Information("Broadcasting to players : Session is about to restart. Next track : " + nextTrack);
                    //TODOadd global broadcast on top of the screen
                    _server.BroadcastPacket(new ChatMessage { SessionId = 255, Message = "Session is about to restart. Next track : " + nextTrack });//check how to grab track name from function, other function + return?
                    serverRestartMessageSent = true;
                }
            }
        }
        //Log.Information("car lastping {info}", _server.EntryCars[0].LastPingTime);
        //Log.Information("car lastpong {info}", _server.EntryCars[0].LastPongTime); 
        //Log.Information("has update to send {info}", _server.EntryCars[0].HasUpdateToSend);

        //Log.Information("");
    }

    public void UpdateServerInfo(ref string trackDownloadLink, ref string welcomeMessage, ref string aboutServer)//rename remove unecessary references
    {
        Update_trackDownloadLink();
        UpdateCMTrackDownload();
        UpdateServerMessage(ref welcomeMessage);
        UpdateEntryList();
        UpdateTrackChecksum();
        // TODO: unload the json/array
        // maybe reload plugins
    }

    public string getSpreadsheetData()///grab the spreadsheet data as a json string
    {
        string jsonstring;
        #pragma warning disable SYSLIB0014 // Type or member is obsolete
        WebClient HttpClient = new();
        #pragma warning restore SYSLIB0014 // Type or member is obsolete

        using (WebClient wc = HttpClient)
        {
            jsonstring = wc.DownloadString("https://opensheet.elk.sh/1PniJavV07JPJQvTcGgrWQp9GQUxFVpkCx7dn8fNjYrw/drift_circuits");

        }
        return jsonstring;
    }


    public void UpdateServerTrack()
    {
        if (NextTrackIsSpecific == false)
        {
        if (true)//to change later but now now symbolises if the track method is set to random
            {
                SetNextTrackRandomly();
            }
        }


        //when testing force track:------------------------------------------------------------------------------------------------------------------------------------------
       // nextTrack = "sunrise_circuit";
        //nextTrack = "ebisu_circuit_touge_course";
        



        NextTrackIsSpecific = false;
        Log.Information("");//just to make it clearer to seee while messing with the plugin
        Log.Information("track name:" + nextTrack);
        _server.Configuration.Server.Track = "csp/1937/../" + nextTrack; //"csp/1937/../"+nextTrack;
        UpdateServerLayout();//need to change in server file as well
        Log.Information("track layout:" + trackLayoutName);
        _server.Configuration.Server.TrackConfig = trackLayoutName;
    }
    public void SetNextTrackRandomly()
    {
        try
        {
            nextTrack = trackList[new Random().Next(0, trackList.Count)]["Tracks"].ToString();
        }
        catch (Exception ex)
        {
            Log.Information("Error executing SetNextTrackRandomly()");
        }
    }
    
    public void UpdateServerLayout()//optimise with Update_trackDownloadLink
    {
        foreach (var objects in trackList)//grab the associated download link
        {
            try
            {
                string trackName = objects["Tracks"].ToString();
                if (trackName == nextTrack)//finds object that matches trackname
                {
                    if (_server.Configuration.ContentConfiguration != null && _server.Configuration.Server.TrackConfig != null && objects["Layouts"] != null)
                    {
                        trackLayoutName = objects["Layouts"].ToString();// grabs the coma separated layouts
                        if (objects["Layouts"].ToString() != null)
                        {
                            string[] layoutNames = objects["Layouts"].ToString().Split(',');
                            trackLayoutName = layoutNames[0];//select the first layout in the list (can always have multiple track + layout 1 ; track + layout 2 in the main track list but this would allow some flexibility for layout selection through command while having just one by default (maybe need to rework this)
                        }
                    }
                    else
                    {
                        trackLayoutName = "";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information("Error executing UpdateServerLayout()");
            }
        }
    }

    public void UpdateTrackChecksum()
    {
        _server.InitializeChecksums();
    }
    public void UpdateEntryList()
    {
        GetTrackJsonPath();
        GetNumberOfPits();
        MatchEntryListToNumberOfPits();
    }
    public void GetTrackJsonPath()
    {
        track_json_path = "content/tracks/csp/" + nextTrack + "/ui/";//ex: D:\....\net6.0\content\tracks\csp\sumisuto_kart_2018\ui\ui_track.json
        if (trackLayoutName != "") { track_json_path += trackLayoutName + "/"; }
        track_json_path += "ui_track.json";
        //Log.Information("The path of track.json is" + track_json_path);
    }

    public void GetNumberOfPits() 
    {
        try
        {
            string track_json = File.ReadAllText(track_json_path);
            JObject track_json_jobject = JObject.Parse(track_json);
            pitboxes = (int)track_json_jobject.SelectToken("pitboxes");
            Log.Information("pitboxes: " + pitboxes.ToString());
        }
        catch (Exception e)
        {
            Log.Information(e.ToString());//TODO: send a message to discord
            UpdateServerTrack();
            UpdateServerInfo(ref trackDownloadLink, ref configWelcomeMessage, ref aboutServer);
        }
    }

    public void MatchEntryListToNumberOfPits()
    {
        dynamicEntryList = null!;
        dynamicEntryList = new EntryList();
        int carcount = _server.Configuration.EntryList.Cars.Count;
        if (carcount <= pitboxes)//if there's more pitboxes than the entry list contains cars, the default entry list is used
        {
            Log.Information("The normal entry list will be used.");
            _server.Configuration.Server.MaxClients = originalEntryList.Cars.Count;
            _server.Configuration.EntryList = originalEntryList;
        }
        else//if there is less pitboxes than cars in the entry list, last cars are removed to fit the number of available pitboxes
        {
            Log.Information("The Dynamic entry list will be used");

            _server.Blacklist.Reloaded += _server.OnBlacklistReloaded;
            _server.Configuration.Server.MaxClients = pitboxes;//doesn't hurt but not necessary for the rest to work rn
            

            _server.EntryCars = new EntryCar[pitboxes];
            Log.Information("Loaded {Count} cars", _server.EntryCars.Length);
            for (int i = 0; i < _server.EntryCars.Length; i++)
            {
                var entry = _server.Configuration.EntryList.Cars[i];
                var driverOptions = CSPDriverOptions.Parse(entry.Skin);
                var aiMode = _server.AiEnabled ? entry.AiMode : AiMode.None;

                _server.EntryCars[i] = new EntryCar(entry.Model, entry.Skin, _server, (byte)i)
                {
                    SpectatorMode = entry.SpectatorMode,
                    Ballast = entry.Ballast,
                    Restrictor = entry.Restrictor,
                    DriverOptionsFlags = driverOptions,
                    AiMode = aiMode,
                    AiEnableColorChanges = driverOptions.HasFlag(DriverOptionsFlags.AllowColorChange),
                    AiControlled = aiMode != AiMode.None,
                    NetworkDistanceSquared = MathF.Pow(_server.Configuration.Extra.NetworkBubbleDistance, 2),
                    OutsideNetworkBubbleUpdateRateMs = 1000 / _server.Configuration.Extra.OutsideNetworkBubbleRefreshRateHz
                };
            }

            _server.ConnectedCars = new ConcurrentDictionary<int, EntryCar>();
            _server.EndpointCars = new ConcurrentDictionary<Address, EntryCar>();
            _server.CurrentSession.Results = new Dictionary<byte, EntryCarResult>();

            foreach (var entryCar in _server.EntryCars)
            {
                _server.CurrentSession.Results.Add(entryCar.SessionId, new EntryCarResult());
            }


            //_server.WeatherProvider = new DefaultWeatherProvider(this);

            //TrackParamsProvider = new IniTrackParamsProvider();
            //TrackParamsProvider.Initialize().Wait();
            _server.TrackParams = _server.TrackParamsProvider.GetParamsForTrack(_server.Configuration.Server.Track);

            NodaTime.DateTimeZone? timeZone;
            if (_server.TrackParams == null)
            {
                if (_server.Configuration.Extra.IgnoreConfigurationErrors.MissingTrackParams)
                {
                    Log.Warning("Using UTC as default time zone");
                    timeZone = NodaTime.DateTimeZone.Utc;
                }
                else
                {
                    throw new ConfigurationException($"No track params found for {_server.Configuration.Server.Track}. More info: https://github.com/compujuckel/AssettoServer/wiki/Common-configuration-errors#missing-track-params");
                }
            }
            else if (string.IsNullOrEmpty(_server.TrackParams.Timezone))
            {
                if (_server.Configuration.Extra.IgnoreConfigurationErrors.MissingTrackParams)
                {
                    Log.Warning("Using UTC as default time zone");
                    timeZone = NodaTime.DateTimeZone.Utc;
                }
                else
                {
                    throw new ConfigurationException($"No time zone found for {_server.Configuration.Server.Track}. More info: https://github.com/compujuckel/AssettoServer/wiki/Common-configuration-errors#missing-track-params");
                }
            }
            else
            {
                timeZone = NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(_server.TrackParams.Timezone);

                if (timeZone == null)
                {
                    throw new ConfigurationException($"Invalid time zone {_server.TrackParams.Timezone} for track {_server.Configuration.Server.Track}. Please enter a valid time zone for your track in cfg/data_track_params.ini.");
                }
            }
            
            //_server.CurrentDateTime = NodaTime.SystemClock.Instance.InZone(timeZone).GetCurrentDate().AtStartOfDayInZone(timeZone).PlusSeconds((long)WeatherUtils.SecondsFromSunAngle(Configuration.Server.SunAngle));//probably not necessary


        }
        
        Log.Information("Entry List: " + "");
        Log.Information("car 0 model:" + carcount.ToString());
    }

    public void Update_trackDownloadLink()//optimise with UpdateServerLayout
    {
        foreach (var objects in trackList)//grab the associated download link
        {
            try
            {
                string trackName = objects["Tracks"].ToString();
                if (trackName == nextTrack)//finds object that matches trackname  MIGHT NEED TO EDIT THAT LATER
                {
                    if (_server.Configuration.ContentConfiguration != null && _server.Configuration.ContentConfiguration.Track != null)
                    {
                        try
                        {
                            trackDownloadLink = objects["Links"].ToString();
                        }
                        catch (Exception e)
                        {
                            Log.Information("ERROR GETTING TRACK LINK");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Information("Error runnning UpdateServerLayout()");
            }
            
        }
    }

    public void UpdateCMTrackDownload()
    {
        _server.Configuration.ContentConfiguration.Track.Url = trackDownloadLink;//updates the cm auto download for the track
    }
        
    public void UpdateServerMessage(ref string welcomeMessage)
    {
        _server.Configuration.Extra.ServerDescription = welcomeMessage + "\n\n" + "Downloads : \nTrack: " + trackDownloadLink + "\n Cars: https://worlddrifttour.com/#street" + "\n\n\n" + aboutServer;
    }

    //related to lua script
    public void reconnectClientsOnTrackChange()
    {
        if (_server != null)
        {
            foreach (var client in _server.ConnectedCars.Values.Select(c => c.Client))
            {
                if (client != null && client.Guid != null)
                {
                    client.Logger.Information("Reconnecting {ClientName}", client.Name);
                    client?.SendPacket(new LUAReconnectClients());
                }
            }
        }
    }
}

public class LUAReconnectClients : IOutgoingNetworkPacket
{
    public void ToWriter(ref PacketWriter writer)
    {
        writer.Write<byte>(0xAB);
        writer.Write<byte>(0x03);
        writer.Write<byte>(255);
        writer.Write<ushort>(60000);
        writer.Write(0xC9F693DA);
        writer.WriteStringWithoutLength("ReconnectClients", Encoding.ASCII);
    }
}

public class TrackCyclePluginCommands : AssettoServer.Commands.ACModuleBase
{
    [Qmmands.Command("nextsession")]
    public void NextSessionCommand()
    {
        //downloadLinkUpdatePlugin.reconnectClientsOnTrackChange();
        Context.Server.NextSession();
    }

    [Qmmands.Command("changetrack")]//TODO update later to support layouts choice
    public void ChangeTrack(string commandParameters)
    {
        downloadLinkUpdatePlugin.NextTrackIsSpecific = true;
        downloadLinkUpdatePlugin.nextTrack = commandParameters;//should stil work after forcing csp version
        Context.Server.NextSession();
    }

    [Qmmands.Command("reconnectclients")]//TODO update later to support layouts choice
    public void ReconnectClients()
    {
       // downloadLinkUpdatePlugin downloadLinkUpdatePlugin.reconnectClientsOnTrackChange();
    }
}
/// <summary>
/// Hello again, I'm sorry you had to go through that. I really am. 
/// </summary>


