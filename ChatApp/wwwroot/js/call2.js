// wwwroot/js/call.js
"use strict";

const callHubConnection = new signalR.HubConnectionBuilder()
    .withUrl("/callHub")
    .withAutomaticReconnect()
    .build();

let pc = null;                 // RTCPeerConnection
let localStream = null;
let remoteStream = null;
let currentCallPartner = null; // username
let currentCallType = null;    // "audio" or "video"

// STUN servers - add TURN if needed for NAT traversal in production
const rtcConfig = {
    iceServers: [
        { urls: "stun:stun.l.google.com:19302" }
    ]
};

// UI elements (assumes IDs from Chat.cshtml)
const voiceBtn = document.getElementById("voiceCallBtn");
const videoBtn = document.getElementById("videoCallBtn");
const incomingModal = document.getElementById("incomingCallModal");
const incomingText = document.getElementById("incomingCallText");
const acceptBtn = document.getElementById("acceptCallBtn");
const rejectBtn = document.getElementById("rejectCallBtn");
const callContainer = document.getElementById("callContainer");
const callWithLabel = document.getElementById("callWithLabel");
const hangupBtn = document.getElementById("hangupBtn");
const localVideoEl = document.getElementById("localVideo");
const remoteVideoEl = document.getElementById("remoteVideo");
const targetLabel = document.getElementById("targetLabel"); // optional: shows current chat target

// Helper to get the currently selected chat target username from your chat UI.
// ADJUST this function to match how you select the chat user in your app.
function getSelectedChatTargetUsername() {
    // In previous chat UI we used selectedTarget object; if not available adapt accordingly.
    if (window.selectedTarget && window.selectedTarget.type === 'user') {
        return window.selectedTarget.name;
    }
    return null;
}

async function startCallAsCaller(targetUsername, callType) {
    if (!targetUsername) { alert("Select a user to call"); return; }
    currentCallPartner = targetUsername;
    currentCallType = callType;

    try {
        // Acquire local media (audio or audio+video)
        localStream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: callType === "video"
        });

        // show local preview
        if (localVideoEl) localVideoEl.srcObject = localStream;

        // create RTCPeerConnection
        await createPeerConnection();

        // add tracks
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        // create offer
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);

        // notify target via SignalR
        await callHubConnection.invoke("CallUser", targetUsername, callType);
        // send offer
        await callHubConnection.invoke("SendOffer", targetUsername, JSON.stringify(offer));

        // show local call window
        showCallUI(targetUsername);
    } catch (err) {
        console.error("startCallAsCaller error:", err);
        cleanupCall();
    }
}

async function createPeerConnection() {
    pc = new RTCPeerConnection(rtcConfig);

    // ensure remote stream container
    remoteStream = new MediaStream();
    if (remoteVideoEl) remoteVideoEl.srcObject = remoteStream;

    // ontrack collects remote media
    pc.ontrack = (evt) => {
        evt.streams.forEach(s => {
            s.getTracks().forEach(t => remoteStream.addTrack(t));
        });
    };

    pc.onicecandidate = (evt) => {
        if (evt.candidate && currentCallPartner) {
            callHubConnection.invoke("SendIceCandidate", currentCallPartner, JSON.stringify(evt.candidate)).catch(console.error);
        }
    };

    pc.onconnectionstatechange = () => {
        console.log("PC state:", pc.connectionState);
        if (pc && (pc.connectionState === "disconnected" || pc.connectionState === "failed" || pc.connectionState === "closed")) {
            cleanupCall();
        }
    };
}

// Called when receiving an offer as callee
async function handleIncomingOffer(fromUsername, offerJson) {
    currentCallPartner = fromUsername;

    // Parse offer
    let offer = JSON.parse(offerJson);

    // Show a modal to accept/reject (populate text)
    incomingText.textContent = `${fromUsername} is calling you (${offer.sdp ? (offer.sdp.includes("m=video") ? "video" : "audio") : ""})`;
    incomingModal.style.display = "block";

    // the actual answer/connection continues when user accepts
}

// Accept call (callee)
async function acceptCall() {
    incomingModal.style.display = "none";

    try {
        // get local media - determine if video should be enabled based on currentCallType?
        // If the callType isn't set, request both audio & video and let users разрешить
        localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });

        if (localVideoEl) localVideoEl.srcObject = localStream;

        await createPeerConnection();

        // add tracks
        localStream.getTracks().forEach(track => pc.addTrack(track, localStream));

        // set remote description - we wait for ReceiveOffer handler to set it before creating answer
        // but if not set yet, ReceiveOffer will set it

        // create answer
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);

        // send answer to caller
        await callHubConnection.invoke("SendAnswer", currentCallPartner, JSON.stringify(answer));
        showCallUI(currentCallPartner);
    } catch (err) {
        console.error("acceptCall error:", err);
        cleanupCall();
    }
}

// Reject call (callee)
async function rejectCall() {
    incomingModal.style.display = "none";
    if (currentCallPartner) {
        await callHubConnection.invoke("RejectCall", currentCallPartner).catch(console.error);
        currentCallPartner = null;
    }
}

// Hang up (either side)
async function hangup() {
    if (currentCallPartner) await callHubConnection.invoke("Hangup", currentCallPartner).catch(console.error);
    cleanupCall();
}

// cleanup local resources and UI
function cleanupCall() {
    try {
        if (pc) {
            pc.ontrack = null;
            pc.onicecandidate = null;
            try { pc.close(); } catch { }
            pc = null;
        }

        if (localStream) {
            localStream.getTracks().forEach(t => t.stop());
            localStream = null;
            if (localVideoEl) localVideoEl.srcObject = null;
        }

        if (remoteStream) {
            remoteStream.getTracks().forEach(t => t.stop());
            remoteStream = null;
            if (remoteVideoEl) remoteVideoEl.srcObject = null;
        }

        currentCallPartner = null;
        currentCallType = null;
        callContainer.style.display = "none";
    } catch (e) {
        console.warn("cleanup error", e);
    }
}

function showCallUI(partner) {
    callWithLabel.textContent = `Call with ${partner}`;
    callContainer.style.display = "block";
}

// ---------- SignalR client handlers ----------

callHubConnection.on("IncomingCall", async (fromUsername, callType) => {
    // Save callType and current partner
    currentCallPartner = fromUsername;
    currentCallType = callType;
    // If you're busy in another call, optionally auto-reject
    if (pc) {
        // busy
        await callHubConnection.invoke("RejectCall", fromUsername).catch(console.error);
        return;
    }
    // Show incoming UI handled in handleIncomingOffer (but we should also show that it's incoming)
    incomingText.textContent = `${fromUsername} is calling (${callType})`;
    incomingModal.style.display = "block";
});

callHubConnection.on("CallFailed", (targetUser, reason) => {
    alert("Call failed: " + reason);
});

callHubConnection.on("CallAccepted", async (byUsername) => {
    // caller: callee accepted - continue. nothing special here because offer/answer exchange will flow
    console.log("Call accepted by", byUsername);
});

callHubConnection.on("CallRejected", (byUsername) => {
    alert(`${byUsername} rejected the call.`);
    cleanupCall();
});

callHubConnection.on("CallEnded", (byUsername) => {
    alert(`${byUsername} ended the call.`);
    cleanupCall();
});

callHubConnection.on("ReceiveOffer", async (fromUsername, offerJson) => {
    // Called on callee side and also when caller gets an offer back (if multi-connection)
    // Ensure incoming modal is hidden (we showed it previously)
    incomingModal.style.display = "none";

    // Save partner and ensure pc exists
    currentCallPartner = fromUsername;

    // create PC if not present
    if (!pc) {
        await createPeerConnection();
    }

    // set remote description
    const offerDesc = JSON.parse(offerJson);
    await pc.setRemoteDescription(offerDesc);

    // Now if we have local stream already (callee accepted), add tracks; otherwise set up on accept
    // Create and send answer (caller actually expects callee to produce answer)
    // But we expect user to accept the call, so we wait for accept action to create answer.
    // Some flows require auto-accept; here we'll wait for accept button to call acceptCall()
});

callHubConnection.on("ReceiveAnswer", async (fromUsername, answerJson) => {
    if (!pc) return;
    const ans = JSON.parse(answerJson);
    await pc.setRemoteDescription(ans);
});

callHubConnection.on("ReceiveIceCandidate", async (fromUsername, candidateJson) => {
    if (!pc) return;
    try {
        const candidate = JSON.parse(candidateJson);
        await pc.addIceCandidate(candidate);
    } catch (e) {
        console.error("addIceCandidate error", e);
    }
});

// ---------- wire UI buttons ----------

if (voiceBtn) {
    voiceBtn.addEventListener("click", async () => {
        const target = getSelectedChatTargetUsername();
        if (!target) { alert("Select a user to call (click a user from the list)"); return; }
        await startCallAsCaller(target, "audio");
    });
}
if (videoBtn) {
    videoBtn.addEventListener("click", async () => {
        const target = getSelectedChatTargetUsername();
        if (!target) { alert("Select a user to call (click a user from the list)"); return; }
        await startCallAsCaller(target, "video");
    });
}

if (acceptBtn) acceptBtn.addEventListener("click", async () => {
    // When user accepts, we must set remote description (we must have received offer already).
    incomingModal.style.display = "none";
    // create peer, add local stream, set remote desc and create send answer handled in acceptCall
    await acceptCall();
});
if (rejectBtn) rejectBtn.addEventListener("click", rejectCall);
if (hangupBtn) hangupBtn.addEventListener("click", hangup);

// Start the hub connection
(async function startHub() {
    try {
        await callHubConnection.start();
        console.log("CallHub connected");
    } catch (err) {
        console.error("CallHub start error:", err);
        setTimeout(startHub, 2000);
    }
})();
