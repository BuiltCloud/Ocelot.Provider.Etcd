﻿namespace Ocelot.Provider.Etcd
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using Configuration.File;
    using Configuration.Repository;
    using dotnet_etcd;
    using global::Consul;
    using Logging;
    using Newtonsoft.Json;
    using Responses;

    public class EtcdFileConfigurationRepository : IFileConfigurationRepository
    {
        private readonly EtcdClient _etcdClient;
        private readonly string _configurationKey;
        private readonly Cache.IOcelotCache<FileConfiguration> _cache;
        private readonly IOcelotLogger _logger;

        public EtcdFileConfigurationRepository(
            Cache.IOcelotCache<FileConfiguration> cache,
            IInternalConfigurationRepository repo,
            IEtcdClientFactory factory,
            IOcelotLoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<EtcdFileConfigurationRepository>();
            _cache = cache;

            var internalConfig = repo.Get();

            _configurationKey = "InternalConfiguration";

            string token = null;

            if (!internalConfig.IsError)
            {
                token = internalConfig.Data.ServiceProviderConfiguration.Token;
                _configurationKey = !string.IsNullOrEmpty(internalConfig.Data.ServiceProviderConfiguration.ConfigurationKey) ?
                    internalConfig.Data.ServiceProviderConfiguration.ConfigurationKey : _configurationKey;
            }

            var config = new EtcdRegistryConfiguration(
                internalConfig.Data.ServiceProviderConfiguration.Host,
                internalConfig.Data.ServiceProviderConfiguration.Port, _configurationKey);

            _etcdClient = factory.Get(config);
        }

        public async Task<Response<FileConfiguration>> Get()
        {
            var config = _cache.Get(_configurationKey, _configurationKey);

            if (config != null)
            {
                return new OkResponse<FileConfiguration>(config);
            }

            var queryResult = await _etcdClient.GetAsync($"{_configurationKey}/FileConfigurations");

            if (queryResult.Response == null)
            {
                return new OkResponse<FileConfiguration>(null);
            }

            var bytes = queryResult.Response.Value;

            var json = Encoding.UTF8.GetString(bytes);

            var consulConfig = JsonConvert.DeserializeObject<FileConfiguration>(json);

            return new OkResponse<FileConfiguration>(consulConfig);
        }

        public async Task<Response> Set(FileConfiguration ocelotConfiguration)
        {
            var json = JsonConvert.SerializeObject(ocelotConfiguration, Formatting.Indented);

            var bytes = Encoding.UTF8.GetBytes(json);

            var kvPair = new KVPair(_configurationKey)
            {
                Value = bytes
            };

            var result = await _etcdClient.KV.Put(kvPair);

            if (result.Response)
            {
                _cache.AddAndDelete(_configurationKey, ocelotConfiguration, TimeSpan.FromSeconds(3), _configurationKey);

                return new OkResponse();
            }

            return new ErrorResponse(new UnableToSetConfigInConsulError($"Unable to set FileConfiguration in consul, response status code from consul was {result.StatusCode}"));
        }
    }
}