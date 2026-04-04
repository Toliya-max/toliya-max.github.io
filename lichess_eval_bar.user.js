// ==UserScript==
// @name         Lichess Local Bot Eval Bar
// @namespace    http://tampermonkey.net/
// @version      1.0
// @description  Fetches live evaluation from the local Python bot and displays it next to the Lichess board.
// @author       You
// @match        *://lichess.org/*
// @grant        GM_xmlhttpRequest
// @connect      127.0.0.1
// @connect      localhost
// ==/UserScript==

(function() {
    'use strict';

    let lastKnownGameId = null;
    let evalContainer = null;
    let evalFill = null;
    let evalText = null;

    // Build the UI
    function createEvalBar() {
        if (document.getElementById('local-bot-eval-container')) return;

        const mainBoard = document.querySelector('cg-container');
        if (!mainBoard) return; // Board not rendered yet

        const cgWrap = document.querySelector('.cg-wrap');
        if (!cgWrap) return;

        // Container
        evalContainer = document.createElement('div');
        evalContainer.id = 'local-bot-eval-container';
        evalContainer.style.position = 'absolute';
        evalContainer.style.right = '-25px';
        evalContainer.style.top = '0';
        evalContainer.style.width = '15px';
        evalContainer.style.height = '100%';
        evalContainer.style.backgroundColor = '#302e2c'; // Lichess dark square color
        evalContainer.style.borderRadius = '3px';
        evalContainer.style.overflow = 'hidden';
        evalContainer.style.display = 'flex';
        evalContainer.style.flexDirection = 'column';
        evalContainer.style.justifyContent = 'flex-end';
        evalContainer.style.zIndex = '100';

        // Fill for White advantage (bottom up)
        evalFill = document.createElement('div');
        evalFill.id = 'local-bot-eval-fill';
        evalFill.style.width = '100%';
        evalFill.style.height = '50%';
        evalFill.style.backgroundColor = '#e0e0e0'; // Light square color
        evalFill.style.transition = 'height 0.3s ease';

        // Text label
        evalText = document.createElement('div');
        evalText.id = 'local-bot-eval-text';
        evalText.style.position = 'absolute';
        evalText.style.width = '100%';
        evalText.style.textAlign = 'center';
        evalText.style.fontSize = '10px';
        evalText.style.fontWeight = 'bold';
        evalText.style.color = '#fff';
        evalText.style.textShadow = '0px 0px 2px #000';
        evalText.style.left = '0';
        // Center text vertically
        evalText.style.top = '50%';
        evalText.style.transform = 'translateY(-50%)';
        evalText.innerText = '0.0';

        evalContainer.appendChild(evalFill);
        evalContainer.appendChild(evalText);
        
        // Append right outside the board
        cgWrap.style.position = 'relative'; // ensure child is absolute to this
        cgWrap.appendChild(evalContainer);
    }

    // Mathematical formula to convert pawn advantage to percentage height
    function scoreToHeightPercent(score) {
        // A standard sigmoid or clamped scaling
        // Let's say +5 is MAX (100%), -5 is MIN (0%), 0 is 50%
        let clamped = Math.max(-5, Math.min(5, score));
        // map [-5, 5] to [0, 100]
        return ((clamped + 5) / 10) * 100;
    }

    function updateEvalUI(score, depth, status) {
        if (!evalFill || !evalText) return;

        if (status === "waiting" || status === "calculating" && score === null) {
            evalText.innerText = "...";
            return;
        }
        
        let displayScore = score.toFixed(1);
        if (score > 900) {
            displayScore = "+M" + Math.ceil(1000 - score);
        } else if (score < -900) {
            displayScore = "-M" + Math.ceil(1000 + score);
        } else if (score > 0) {
            displayScore = "+" + displayScore;
        }

        evalText.innerText = displayScore + "\nd" + (depth || 0);

        // Calculate height. If board is flipped, we might want to invert this.
        // Assuming standard view (White at bottom), white advantage = more white fill (larger height).
        const isFlipped = document.querySelector('.cg-wrap.orientation-black') !== null;
        
        let percent = scoreToHeightPercent(score);
        if (isFlipped) {
            percent = 100 - percent;
            evalFill.style.backgroundColor = '#302e2c'; // dark if flipped
            evalContainer.style.backgroundColor = '#e0e0e0'; 
        } else {
            evalFill.style.backgroundColor = '#e0e0e0'; // light
            evalContainer.style.backgroundColor = '#302e2c'; 
        }

        evalFill.style.height = percent + '%';
        
        // Adjust text format based on color it sits on
        if (percent > 60) {
            evalText.style.color = '#333';
            evalText.style.textShadow = 'none';
        } else {
            evalText.style.color = '#fff';
            evalText.style.textShadow = '0px 0px 2px #000';
        }
    }

    function extractGameId() {
        const path = window.location.pathname;
        const match = path.match(/^\/([a-zA-Z0-9]{8,12})/); // standard game URLs are 8 chars or 12
        if (match) {
            // Drop the extra 4 chars if it's playing URL (12 chars total to 8 char base gameId)
            return match[1].substring(0, 8);
        }
        return null; // Not on a game page
    }

    function pollEval() {
        const gameId = extractGameId();
        if (!gameId) {
            if (evalContainer) evalContainer.style.display = 'none';
            return; // Only poll on game pages
        }

        createEvalBar();
        if (evalContainer) evalContainer.style.display = 'flex';

        GM_xmlhttpRequest({
            method: "GET",
            url: "http://127.0.0.1:8282/eval?game=" + gameId,
            onload: function(response) {
                if (response.status === 200) {
                    try {
                        const data = JSON.parse(response.responseText);
                        updateEvalUI(data.score, data.depth, data.status);
                    } catch (e) {
                        console.error("Parse error", e);
                    }
                }
            }
        });
    }

    // Start polling every 500ms
    setInterval(pollEval, 500);

})();
