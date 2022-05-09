(function () {
    var computedStyle = window.getComputedStyle(document.documentElement);
    var fontSize = parseInt(computedStyle.fontSize);
    function getRemHeight(ele) {
        return ele.clientHeight / fontSize;
    }

    var expandLabel = "▼ Expand";
    var hideLabel = "▲ Hide";
    var hiddenClass = "hidden";
    function injectExpandLink(ele) {
        var expand = document.createElement("a");
        expand.innerText = expandLabel;
        expand.className = "expand";
        expand.href = "#";
        var isExpanded = false;
        expand.onclick = function () {
            if (isExpanded) {
                ele.classList.add(hiddenClass);
                expand.innerText = expandLabel;
            }
            else {
                ele.classList.remove(hiddenClass);
                expand.innerText = hideLabel;
            }
            isExpanded = !isExpanded;
            return false;
        };
        ele.parentElement.insertBefore(expand, ele);
        ele.classList.add(hiddenClass);
    }

    var topLevelElements = document.querySelectorAll("body > *");
    for (var i = 0; i < topLevelElements.length; i++) {
        var ele = topLevelElements[i];
        if (getRemHeight(ele) > 4) {
            injectExpandLink(ele);
        }
    }
}());