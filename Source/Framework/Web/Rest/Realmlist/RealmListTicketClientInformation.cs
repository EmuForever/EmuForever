// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace Framework.Web
{
    public class RealmListTicketClientInformation
    {
        [JsonPropertyName("info")]
        public ClientInformation Info { get; set; } = new ClientInformation();
    }
}
