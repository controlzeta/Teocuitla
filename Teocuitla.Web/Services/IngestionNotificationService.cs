namespace Teocuitla.Web.Services
{
    public class IngestionNotificationEventArgs : EventArgs
    {
        public string Sku { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string Dominio { get; set; } = string.Empty;
    }

    public class IngestionNotificationService
    {
        public event EventHandler<IngestionNotificationEventArgs>? OnIngestionReceived;

        public void NotifyIngestion(string sku, string nombre, decimal precio, string dominio)
        {
            OnIngestionReceived?.Invoke(this, new IngestionNotificationEventArgs
            {
                Sku = sku,
                Nombre = nombre,
                Precio = precio,
                Dominio = dominio
            });
        }
    }
}
