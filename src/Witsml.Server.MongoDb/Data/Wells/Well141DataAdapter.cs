﻿using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Energistics.DataAccess;
using Energistics.DataAccess.WITSML141;
using Energistics.Datatypes;
using PDS.Witsml.Server.Configuration;

namespace PDS.Witsml.Server.Data.Wells
{
    /// <summary>
    /// Data adapter that encapsulates CRUD functionality for <see cref="Well" />
    /// </summary>
    /// <seealso cref="PDS.Witsml.Server.Data.MongoDbDataAdapter{Energistics.DataAccess.WITSML141.Well}" />
    /// <seealso cref="PDS.Witsml.Server.Configuration.IWitsml141Configuration" />
    [Export(typeof(IWitsml141Configuration))]
    [Export(typeof(IWitsmlDataAdapter<Well>))]
    [Export(typeof(IEtpDataAdapter<Well>))]
    [Export141(ObjectTypes.Well, typeof(IEtpDataAdapter))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class Well141DataAdapter : MongoDbDataAdapter<Well>, IWitsml141Configuration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Well141DataAdapter"/> class.
        /// </summary>
        /// <param name="databaseProvider">The database provider.</param>
        [ImportingConstructor]
        public Well141DataAdapter(IDatabaseProvider databaseProvider) : base(databaseProvider, ObjectNames.Well141)
        {
            Logger.Debug("Instance created.");
        }

        /// <summary>
        /// Gets the supported capabilities for the <see cref="Well"/> object.
        /// </summary>
        /// <param name="capServer">The capServer object.</param>
        public void GetCapabilities(CapServer capServer)
        {
            Logger.DebugFormat("Getting capabilities for server '{0}'.", capServer.Name);

            capServer.Add(Functions.GetFromStore, ObjectTypes.Well);
            capServer.Add(Functions.AddToStore, ObjectTypes.Well);
            capServer.Add(Functions.UpdateInStore, ObjectTypes.Well);
            capServer.Add(Functions.DeleteFromStore, ObjectTypes.Well);
        }

        /// <summary>
        /// Queries the object(s) specified by the parser.
        /// </summary>
        /// <param name="parser">The parser that specifies the query parameters.</param>
        /// <returns>Queried objects.</returns>
        public override WitsmlResult<IEnergisticsCollection> Query(WitsmlQueryParser parser)
        {
            var returnElements = parser.ReturnElements();
            Logger.DebugFormat("Querying with return elements '{0}'", returnElements);

            var fields = (OptionsIn.ReturnElements.IdOnly.Equals(returnElements))
                ? new List<string> { IdPropertyName, NamePropertyName }
                : null;

            return new WitsmlResult<IEnergisticsCollection>(
                ErrorCodes.Success,
                new WellList()
                {
                    Well = QueryEntities(parser, fields)
                });
        }

        /// <summary>
        /// Adds a <see cref="Well"/> to the data store.
        /// </summary>
        /// <param name="entity">The <see cref="Well"/> to be added.</param>
        /// <returns>
        /// A WITSML result that includes a positive value indicates a success or a negative value indicates an error.
        /// </returns>
        public override WitsmlResult Add(Well entity)
        {
            entity.Uid = NewUid(entity.Uid);
            entity.CommonData = entity.CommonData.Create();
            Logger.DebugFormat("Adding Well with uid '{0}' and name '{1}'.", entity.Uid, entity.Name);

            Validate(Functions.AddToStore, entity);
            Logger.DebugFormat("Validated Well with uid '{0}' and name {1} for Add", entity.Uid, entity.Name);

            InsertEntity(entity);

            return new WitsmlResult(ErrorCodes.Success, entity.Uid);
        }

        /// <summary>
        /// Gets a collection of data objects related to the specified URI.
        /// </summary>
        /// <param name="parentUri">The parent URI.</param>
        /// <returns>A collection of data objects.</returns>
        public override List<Well> GetAll(EtpUri? parentUri = null)
        {
            Logger.Debug("Fetching all Wells.");

            return GetQuery()
                .OrderBy(x => x.Name)
                .ToList();
        }

        /// <summary>
        /// Parses the specified XML string.
        /// </summary>
        /// <param name="xml">The XML string.</param>
        /// <returns>An instance of <see cref="Well" />.</returns>
        protected override Well Parse(string xml)
        {
            var list = WitsmlParser.Parse<WellList>(xml);
            return list.Well.FirstOrDefault();
        }
    }
}
