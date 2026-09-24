(function () {
  var grid = document.getElementById('live-grid');
  if (!grid) return;

  setInterval(async function () {
    try {
      var response = await fetch('/Matches/LiveJson');
      if (!response.ok) return;

      var matches = await response.json();
      matches.forEach(function (match) {
        var card = grid.querySelector('[href$="/' + match.matchId + '"]');
        if (!card) return;

        var score = card.querySelector('.score');
        var status = card.querySelector('.status');

        if (score && match.homeGoals !== null && match.awayGoals !== null) {
          score.textContent = match.homeGoals + ' - ' + match.awayGoals;
        }
        if (status) {
          status.textContent = match.status;
        }
      });
    } catch (err) {
      console.warn('Canlı skor yenilenemedi', err);
    }
  }, 60000);
})();

(function () {
  function token() {
    var el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
  }

  async function toggle(url, body, btn) {
    try {
      var res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
          'RequestVerificationToken': token()
        },
        body: body
      });

      if (res.status === 401 || res.redirected) {
        window.location.href = '/Account/Login';
        return;
      }
      if (!res.ok) return;

      var data = await res.json();
      btn.classList.toggle('on', data.isFavorite);
    } catch (err) {
      console.warn('Favori guncellenemedi', err);
    }
  }

  document.addEventListener('click', function (e) {
    var matchBtn = e.target.closest('.fav-btn');
    if (matchBtn) {
      e.preventDefault();
      e.stopPropagation();
      toggle('/Favorites/ToggleMatch', 'matchId=' + matchBtn.dataset.match, matchBtn);
      return;
    }

    var teamBtn = e.target.closest('.fav-team');
    if (teamBtn) {
      e.preventDefault();
      e.stopPropagation();
      toggle('/Favorites/ToggleTeam', 'teamId=' + teamBtn.dataset.team, teamBtn);
    }
  });
})();
