(function () {
  "use strict";

  dragula([
    document.querySelector("#leads-discovered"),
    document.querySelector("#leads-qualified"),
    document.querySelector("#contact-initiated"),
    document.querySelector("#needs-identified"),
    document.querySelector("#negotiation"),
    document.querySelector("#deal-finalized"),
  ]);
})();
