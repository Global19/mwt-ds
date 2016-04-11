﻿using Microsoft.Research.MultiWorldTesting.ClientLibrary.VW;
using Microsoft.Research.MultiWorldTesting.Contract;
using Microsoft.Research.MultiWorldTesting.ExploreLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using VW;

namespace Microsoft.Research.MultiWorldTesting.ClientLibrary
{
    internal class VWPolicy<TContext> 
        : VWBaseContextMapper<VowpalWabbitThreadedPrediction<TContext>, VowpalWabbit<TContext>, TContext, int>, IPolicy<TContext>
    {
        /// <summary>
        /// Constructor using a memory stream.
        /// </summary>
        /// <param name="vwModelStream">The VW model memory stream.</param>
        internal VWPolicy(Stream vwModelStream = null, VowpalWabbitFeatureDiscovery featureDiscovery = VowpalWabbitFeatureDiscovery.Json)
            : base(vwModelStream, featureDiscovery)
        {
        }

        protected override PolicyDecision<int> MapContext(VowpalWabbit<TContext> vw, TContext context)
        {
            var action = (int)vw.Predict(context, VowpalWabbitPredictionType.CostSensitive);
            var state = new VWState { ModelId = vw.Native.ID };

            return PolicyDecision.Create(action, state);
        }
    }

    public static class VWPolicy
    {
        public static DecisionServiceConfigurationWrapper<string, int> CreateJsonPolicy(DecisionServiceConfiguration config)
        {
            config.UseJsonContext = true;
            return VWPolicy.Wrap(config, new VWJsonPolicy(config.ModelStream));
        }

        public static DecisionServiceConfigurationWrapper<string, int[]> CreateJsonRanker(DecisionServiceConfiguration config)
        {
            config.UseJsonContext = true;
            return VWPolicy.Wrap(config, new VWJsonRanker(config.ModelStream));
        }

        public static DecisionServiceConfigurationWrapper<TContext, int> CreatePolicy<TContext>(DecisionServiceConfiguration config)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(config, new VWPolicy<TContext>(config.ModelStream, config.FeatureDiscovery));
        }

        public static DecisionServiceConfigurationWrapper<TContext, int[]> CreateRanker<TContext>(DecisionServiceConfiguration config)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(config, new VWRanker<TContext>(config.ModelStream, config.FeatureDiscovery));
        }

        public static DecisionServiceConfigurationWrapper<TContext, int[]>
        CreateRanker<TContext, TActionDependentFeature>(
            DecisionServiceConfiguration config,
            Func<TContext, IReadOnlyCollection<TActionDependentFeature>> getContextFeaturesFunc)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(config, new VWRanker<TContext, TActionDependentFeature>(getContextFeaturesFunc, config.ModelStream, config.FeatureDiscovery));
        }
        
        public static DecisionServiceConfigurationWrapper<string, int>
        StartWithJsonPolicy(
            DecisionServiceConfiguration config,
            IContextMapper<string, int> initialPolicy = null)
        {
            config.UseJsonContext = true;
            return VWPolicy.Wrap(config, new VWJsonPolicy(config.ModelStream), initialPolicy);
        }

        public static DecisionServiceConfigurationWrapper<string, int[]>
        StartWithJsonRanker(
            DecisionServiceConfiguration config,
            IContextMapper<string, int[]> initialPolicy = null)
        {
            config.UseJsonContext = true;
            return VWPolicy.Wrap(config, new VWJsonRanker(config.ModelStream), initialPolicy);
        }

        public static DecisionServiceConfigurationWrapper<TContext, int>
        StartWithPolicy<TContext>(
            DecisionServiceConfiguration config,
            IContextMapper<TContext, int> initialPolicy = null)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(config, new VWPolicy<TContext>(config.ModelStream, config.FeatureDiscovery), initialPolicy);
        }

        public static DecisionServiceConfigurationWrapper<TContext, int[]>
        StartWithRanker<TContext>(
            DecisionServiceConfiguration config,
            IContextMapper<TContext, int[]> initialPolicy = null)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(config, new VWRanker<TContext>(config.ModelStream, config.FeatureDiscovery), initialPolicy);
        }

        public static DecisionServiceConfigurationWrapper<TContext, int[]>
        StartWithRanker<TContext, TActionDependentFeature>(
            DecisionServiceConfiguration config,
            Func<TContext, IReadOnlyCollection<TActionDependentFeature>> getContextFeaturesFunc,
            IContextMapper<TContext, int[]> initialPolicy = null)
        {
            config.UseJsonContext = false;
            return VWPolicy.Wrap(
                config, 
                new VWRanker<TContext, TActionDependentFeature>(getContextFeaturesFunc, config.ModelStream, config.FeatureDiscovery),
                initialPolicy);
        }

        
        public static DecisionServiceConfigurationWrapper<TContext, TPolicyValue>
            Wrap<TContext, TPolicyValue>(
                DecisionServiceConfiguration config,
                IContextMapper<TContext, TPolicyValue> vwPolicy,
                IContextMapper<TContext, TPolicyValue> defaultPolicy = null)
        {
            var metaData = GetBlobLocations(config);
            var ucm = new DecisionServiceConfigurationWrapper<TContext, TPolicyValue> 
            { 
                Configuration = config,
                Metadata = metaData,
                DefaultPolicy = defaultPolicy
            };

            // conditionally wrap if it can be updated.
            var updatableContextMapper = vwPolicy as IUpdatable<Stream>;

            IContextMapper<TContext, TPolicyValue> policy;

            if (config.OfflineMode || metaData == null || updatableContextMapper == null)
                policy = vwPolicy;
            else
            {
                var dsPolicy = new DecisionServicePolicy<TContext, TPolicyValue>(vwPolicy, config, metaData);
                dsPolicy.Subscribe(ucm);
                policy = dsPolicy;
            }
            ucm.InternalPolicy = policy;

            return ucm;
        }

        internal static ApplicationTransferMetadata GetBlobLocations(DecisionServiceConfiguration config)
        {
            if (config.OfflineMode)
                return null;

            string redirectionBlobLocation = string.Format(DecisionServiceConstants.RedirectionBlobLocation, config.AuthorizationToken);

            try
            {
                using (var wc = new WebClient())
                {
                    string jsonMetadata = wc.DownloadString(redirectionBlobLocation);
                    return JsonConvert.DeserializeObject<ApplicationTransferMetadata>(jsonMetadata);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Unable to retrieve blob locations from storage using the specified token", ex);
            }
        }
    }
}