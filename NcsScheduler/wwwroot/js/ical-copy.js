function copyIcalFeedUrl(inputId, btn) {
    var url = document.getElementById(inputId);
    navigator.clipboard.writeText(url.value).then(function () {
        var original = btn.textContent;
        btn.textContent = 'Copied!';
        btn.classList.replace('btn-outline-primary', 'btn-success');
        setTimeout(function () {
            btn.textContent = original;
            btn.classList.replace('btn-success', 'btn-outline-primary');
        }, 2000);
    });
}
