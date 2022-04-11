﻿using AssettoServer.Server;
using AssettoServer.Server.TrackParams;
using AssettoServer.Server.Weather;
using Serilog;

namespace LiveWeatherPlugin;

public class LiveWeatherProvider
{
    private readonly ACServer _server;
    private readonly LiveWeatherConfiguration _configuration;
    private readonly OpenWeatherMapWeatherProvider _liveWeatherProvider;

    private readonly TrackParams _trackParams; 

    public LiveWeatherProvider(ACServer server, LiveWeatherConfiguration configuration)
    {
        _server = server;
        _configuration = configuration;

        if (string.IsNullOrWhiteSpace(_configuration.OpenWeatherMapApiKey))
            throw new InvalidOperationException("OpenWeatherMap API key not set. Cannot enable live weather");
        if (_server.TrackParams == null)
            throw new InvalidOperationException("No track params set for track");

        _liveWeatherProvider = new OpenWeatherMapWeatherProvider(_configuration.OpenWeatherMapApiKey);
        _trackParams = _server.TrackParams;
    }

    internal async Task LoopAsync()
    {
        while (true)
        {
            try
            {
                await UpdateAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during live weather update");
            }
            finally
            {
                await Task.Delay(_configuration.UpdateIntervalMilliseconds);
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task UpdateAsync()
    {
        var last = _server.CurrentWeather;
        var response = await _liveWeatherProvider.GetWeatherAsync(_trackParams.Latitude, _trackParams.Longitude);
        var weatherType = _server.WeatherTypeProvider.GetWeatherType(response.WeatherType);
            
        Log.Debug("Live weather: {WeatherType}, ambient {TemperatureAmbient}°C", response.WeatherType, response.TemperatureAmbient);

        _server.SetWeather(new WeatherData(last.Type, weatherType)
        {
            TransitionDuration = 120000.0,
            TemperatureAmbient = response.TemperatureAmbient,
            TemperatureRoad = (float)WeatherUtils.GetRoadTemperature(_server.CurrentDateTime.TimeOfDay.TickOfDay / 10_000_000.0, response.TemperatureAmbient,
                weatherType.TemperatureCoefficient),
            Pressure = response.Pressure,
            Humidity = response.Humidity,
            WindSpeed = response.WindSpeed,
            WindDirection = response.WindDirection,
            RainIntensity = last.RainIntensity,
            RainWetness = last.RainWetness,
            RainWater = last.RainWater,
            TrackGrip = last.TrackGrip
        });
    }
}