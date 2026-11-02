using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace Academy.Api.Hosting;

// XML contains certificate-encrypted key material; runtime needs SELECT/INSERT only.
public sealed class PostgresKeyRepository(string connectionString) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = new NpgsqlConnection(connectionString); connection.Open();
        using var command = new NpgsqlCommand("SELECT xml FROM app_data_protection_keys ORDER BY id", connection);
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read()) elements.Add(XElement.Parse(reader.GetString(0)));
        return elements;
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = new NpgsqlConnection(connectionString); connection.Open();
        using var command = new NpgsqlCommand("INSERT INTO app_data_protection_keys (xml) VALUES (@xml)", connection);
        command.Parameters.AddWithValue("xml", element.ToString(SaveOptions.DisableFormatting));
        command.ExecuteNonQuery();
    }
}
