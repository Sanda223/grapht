function highlightCard(id) {
    const el = document.getElementById('order-' + id);
    if (el) el.classList.add('highlight');
}

const dismissCard = (id) => {
    document.getElementById('order-' + id)?.classList.remove('highlight');
};

async function notifyOrderChanged(orderId) {
    await DotNet.invokeMethodAsync('SampleApp.Web', 'RefreshOrders', orderId);
}
