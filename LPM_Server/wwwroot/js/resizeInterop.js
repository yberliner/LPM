window.resizeFunctions = {
    addresizeListener: function (dotNetReference) {
        window.resizeFunctions.resizeHandler = () => {
            // .NET expects int — Math.round to handle browser-zoom fractional widths.
            dotNetReference.invokeMethodAsync('OnWindowResize', Math.round(window.innerWidth));
        };
        
        // Add the scroll event listener
        window.addEventListener("resize", window.resizeFunctions.resizeHandler);

        // Return the scroll handler function for detaching later
        return window.resizeFunctions.resizeHandler;
    },
    detachScrollListener: function () {
        // Remove the scroll event listener
        window.removeEventListener("resize", window.resizeFunctions.resizeHandler);
    }
};
