// The page's address. ?seed=... pins a map (a testing aid, README); going back to the lobby drops it from the
// address bar too, so that a reload does not bring the pinned map back (G44 bug report: "refreshing doesn't give
// me a new seed").
mergeInto(LibraryManager.library, {

  PH_ClearQuery: function () {
    try { if (window.location.search) window.history.replaceState(null, '', window.location.pathname); } catch (e) { }
  },

});
