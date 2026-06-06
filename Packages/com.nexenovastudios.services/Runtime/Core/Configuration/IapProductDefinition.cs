#nullable enable
using System;
using UnityEngine;

namespace Nexenova.Services
{
    /// <summary>
    /// One product in the IAP catalog. The live catalog comes from Remote Config
    /// (key <c>ConfigKeys.IapCatalog</c>); this serializable shape doubles as the
    /// baked-in fallback authored on <see cref="ServicesSettings"/>.
    /// </summary>
    [Serializable]
    public sealed class IapProductDefinition
    {
        [SerializeField] private string productId = string.Empty;
        [SerializeField] private CatalogProductType productType = CatalogProductType.Consumable;

        [Tooltip("JSON array applied by the grant pipeline on purchase, e.g. [{\"currencyId\":\"GEMS\",\"amount\":100}]")]
        [SerializeField] private string grantsJson = "[]";

        public string ProductId
        {
            get => productId;
            set => productId = value;
        }

        public CatalogProductType ProductType
        {
            get => productType;
            set => productType = value;
        }

        public string GrantsJson
        {
            get => grantsJson;
            set => grantsJson = value;
        }
    }
}
